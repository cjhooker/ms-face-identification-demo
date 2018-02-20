using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FaceRecognitionDemo
{
    class PersonGroupLoader
    {
        private int _maxConcurrentProcesses = 4;

        public string GroupName { get; set; }

        public FaceServiceClient FaceServiceClient { get; set; }
        public MainWindow MainWindow { get; set; }

        public PersonGroupLoader(FaceServiceClient faceServiceClient, string groupName, MainWindow mainWindow)
        {
            GroupName = groupName;
            FaceServiceClient = faceServiceClient;
            MainWindow = mainWindow;
        }

        /// <summary>
        /// Pick the root person database folder, to minimum the data preparation logic, the folder should be under following construction
        /// Each person's image should be put into one folder named as the person's name
        /// All person's image folder should be put directly under the root person database folder
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event argument</param>
        public async void Load()
        {
            bool groupExists = false;

            // Test whether the group already exists
            try
            {
                await FaceServiceClient.GetPersonGroupAsync(GroupName);
                groupExists = true;
            }
            catch (FaceAPIException ex)
            {
                if (ex.ErrorCode != "PersonGroupNotFound")
                {
                    MainWindow.LoaderStatusLabel.Content = "Error";
                    return;
                }
            }

            // If group exists, warn user it will be replaced
            if (groupExists)
            {
                var cleanGroup = System.Windows.MessageBox.Show(string.Format("Requires a clean up for group \"{0}\" before setting up a new person database. Click OK to proceed, group \"{0}\" will be cleared.", GroupName), "Warning", MessageBoxButton.OKCancel);
                if (cleanGroup == MessageBoxResult.OK)
                {
                    MainWindow.LoaderStatusLabel.Content = "Removing Group";
                    await FaceServiceClient.DeletePersonGroupAsync(GroupName);
                }
                else
                {
                    return;
                }
            }

            // Show folder picker
            System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
            var result = dlg.ShowDialog();

            // Set the suggestion count is intent to minimum the data preparation step only,
            // it's not corresponding to service side constraint
            const int SuggestionCount = 15;

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                // User picked a root person database folder

                // Call create person group REST API
                // Create person group API call will failed if group with the same name already exists
                MainWindow.LoaderStatusLabel.Content = "Creating Group";
                try
                {
                    await FaceServiceClient.CreatePersonGroupAsync(GroupName, GroupName);
                    MainWindow.LoaderStatusLabel.Content = "Group Created";
                }
                catch (FaceAPIException)
                {
                    MainWindow.LoaderStatusLabel.Content = "Error";
                    return;
                }

                int processCount = 0;
                bool forceContinue = false;

                MainWindow.LoaderStatusLabel.Content = "Processing images";

                // Enumerate top level directories, each directory contains one person's images
                int invalidImageCount = 0;
                foreach (var dir in System.IO.Directory.EnumerateDirectories(dlg.SelectedPath))
                {
                    var tasks = new List<Task>();
                    var tag = System.IO.Path.GetFileName(dir);
                    var personName = tag;
                    var faces = new ObservableCollection<Face>();

                    // Call create person REST API, the new create person id will be returned
                    var personId = (await FaceServiceClient.CreatePersonAsync(GroupName, personName)).PersonId.ToString();

                    string img;
                    // Enumerate images under the person folder, call detection
                    var imageList = new ConcurrentBag<string>(
                                        Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                                    .Where(s => s.ToLower().EndsWith(".jpg") || s.ToLower().EndsWith(".png") || s.ToLower().EndsWith(".bmp") || s.ToLower().EndsWith(".gif")));

                    while (imageList.TryTake(out img))
                    {
                        tasks.Add(Task.Factory.StartNew(
                            async (obj) =>
                            {
                                var imgPath = obj as string;

                                using (var fStream = File.OpenRead(imgPath))
                                {
                                    try
                                    {
                                        // Update person faces on server side
                                        var persistFace = await FaceServiceClient.AddPersonFaceAsync(GroupName, Guid.Parse(personId), fStream, imgPath);
                                        return new Tuple<string, Microsoft.ProjectOxford.Face.Contract.AddPersistedFaceResult>(imgPath, persistFace);
                                    }
                                    catch (FaceAPIException ex)
                                    {
                                        // if operation conflict, retry.
                                        if (ex.ErrorCode.Equals("ConcurrentOperationConflict"))
                                        {
                                            imageList.Add(imgPath);
                                            return null;
                                        }
                                        // if operation cause rate limit exceed, retry.
                                        else if (ex.ErrorCode.Equals("RateLimitExceeded"))
                                        {
                                            imageList.Add(imgPath);
                                            return null;
                                        }
                                        else if (ex.ErrorMessage.Contains("more than 1 face in the image."))
                                        {
                                            Interlocked.Increment(ref invalidImageCount);
                                        }
                                        // Here we simply ignore all detection failure in this sample
                                        // You may handle these exceptions by check the Error.Error.Code and Error.Message property for ClientException object
                                        return new Tuple<string, Microsoft.ProjectOxford.Face.Contract.AddPersistedFaceResult>(imgPath, null);
                                    }
                                }
                            },
                            img).Unwrap());

                        if (processCount >= SuggestionCount && !forceContinue)
                        {
                            var continueProcess = System.Windows.Forms.MessageBox.Show("The images loaded have reached the recommended count, may take long time if proceed. Would you like to continue to load images?", "Warning", System.Windows.Forms.MessageBoxButtons.YesNo);
                            if (continueProcess == System.Windows.Forms.DialogResult.Yes)
                            {
                                forceContinue = true;
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (tasks.Count >= _maxConcurrentProcesses || imageList.IsEmpty)
                        {
                            await Task.WhenAll(tasks);
                            tasks.Clear();
                        }
                    }
                    
                }

                try
                {
                    // Start train person group
                    MainWindow.LoaderStatusLabel.Content = "Training Person Group";
                    await FaceServiceClient.TrainPersonGroupAsync(GroupName);

                    // Wait until train completed
                    while (true)
                    {
                        await Task.Delay(1000);
                        var status = await FaceServiceClient.GetPersonGroupTrainingStatusAsync(GroupName);
                        if (status.Status != Microsoft.ProjectOxford.Face.Contract.Status.Running)
                        {
                            break;
                        }
                    }
                }
                catch (FaceAPIException)
                {
                    MainWindow.LoaderStatusLabel.Content = "Error";
                }
            }
            MainWindow.LoaderStatusLabel.Content = "Done";
            GC.Collect();
        }
    }
}
