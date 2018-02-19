using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VideoFrameAnalyzer;

namespace FaceRecognitionDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private FaceServiceClient _faceClient = null;
        private readonly FrameGrabber<LiveCameraResult> _grabber = null;
        private Person[] _persons = null;
        private const string GroupName = "test-group-2";

        private readonly string _isolatedStorageSubscriptionKeyFileName = "Subscription.txt";
        private readonly string _isolatedStorageSubscriptionEndpointFileName = "SubscriptionEndpoint.txt";

        private readonly string _defaultSubscriptionKeyPromptMessage = "Paste your subscription key here";
        private readonly string _defaultSubscriptionEndpointPromptMessage = "Paste your endpoint here";
        
        public string SubscriptionKey { get; set; }

        public string SubscriptionEndpoint { get; set; }
        
        public MainWindow()
        {
            InitializeComponent();

            SubscriptionKey = GetSubscriptionKeyFromIsolatedStorage();
            SubscriptionEndpoint = GetSubscriptionEndpointFromIsolatedStorage();

            SubscriptionKeyTextBox.Text = SubscriptionKey;
            SubscriptionEndpointTextBox.Text = SubscriptionEndpoint;

            _grabber = new FrameGrabber<LiveCameraResult>
            {
                AnalysisFunction = IdentifyFaceFunction
            };
            _grabber.TriggerAnalysisOnInterval(new TimeSpan(0, 0, 2));
        }

        private void OnWindowLoad(object sender, RoutedEventArgs eventArgs)
        {
            // Set up a listener for when the client receives a new frame.
            _grabber.NewFrameProvided += (s, e) =>
            {
                // The callback may occur on a different thread, so we must use the
                // MainWindow.Dispatcher when manipulating the UI. 
                this.Dispatcher.BeginInvoke((Action)(() =>
                {
                    // Display the image from the camera
                    CameraImage.Source = e.Frame.Image.ToBitmapSource();
                }));
            };

            // Set up a listener for when the client receives a new result from an API call. 
            _grabber.NewResultAvailable += (s, e) =>
            {
                ClearLog();

                var noFaces = true;

                foreach (var person in e.Analysis.PeopleIdentified)
                {
                    Log($"Identified {person}");
                    noFaces = false;
                }

                if (e.Analysis.UnknownFaceCount > 0)
                {
                    Log($"{e.Analysis.UnknownFaceCount} unknown faces detected");
                    noFaces = false;
                }

                if (noFaces)
                {
                    Log("No faces detected");
                }
            };
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _faceClient = new FaceServiceClient(SubscriptionKey, SubscriptionEndpoint);
            _persons = await _faceClient.ListPersonsAsync(GroupName);

            await StartCamera();
        }

        /// <summary> Function which submits a frame to the Face API. </summary>
        /// <param name="frame"> The video frame to submit. </param>
        /// <returns> A <see cref="Task{LiveCameraResult}"/> representing the asynchronous API call,
        ///     and containing the faces returned by the API. </returns>
        private async Task<LiveCameraResult> IdentifyFaceFunction(VideoFrame frame)
        {
            var imageStream = frame.Image.ToMemoryStream();
            var result = new LiveCameraResult();

            try
            {
                // First detect any faces
                var faces = await _faceClient.DetectAsync(imageStream);

                if (faces.Length <= 0)
                {
                    return result;
                }

                // Identify each face
                // Call identify REST API, the result contains identified person information
                var identifyResult = await _faceClient.IdentifyAsync(GroupName, faces.Select(ff => ff.FaceId).ToArray());
                for (int idx = 0; idx < faces.Length; idx++)
                {
                    // Update identification result for rendering
                    var res = identifyResult[idx];
                    if (res.Candidates.Length > 0 && _persons.Any(p => p.PersonId == res.Candidates[0].PersonId))
                    {
                        var personName = _persons.Where(p => p.PersonId == res.Candidates[0].PersonId).First().Name;
                        await _grabber.StopProcessingAsync();
                        result.PeopleIdentified.Add(personName);
                    }
                    else
                    {
                        result.UnknownFaceCount++;
                    }
                }
            }
            catch (FaceAPIException ex)
            {
                Log($"Response: {ex.ToString()}");
            }

            return result;
        }

        private void Log(string text)
        {
            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (MessageArea.Text != "")
                {
                    MessageArea.Text += "\n";
                }
                MessageArea.Text += $"{text}";
            }));
        }

        private void ClearLog()
        {
            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                MessageArea.Text = "";
            }));
        }

        private async Task StartCamera()
        {
            await _grabber.StartProcessingCameraAsync(0);
        }

        /// <summary>
        /// Gets the subscription key from isolated storage.
        /// </summary>
        /// <returns></returns>
        private string GetSubscriptionKeyFromIsolatedStorage()
        {
            string subscriptionKey = null;

            using (IsolatedStorageFile isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null))
            {
                try
                {
                    using (var iStream = new IsolatedStorageFileStream(_isolatedStorageSubscriptionKeyFileName, FileMode.Open, isoStore))
                    {
                        using (var reader = new StreamReader(iStream))
                        {
                            subscriptionKey = reader.ReadLine();
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                    subscriptionKey = null;
                }
            }
            if (string.IsNullOrEmpty(subscriptionKey))
            {
                subscriptionKey = _defaultSubscriptionKeyPromptMessage;
            }
            return subscriptionKey;
        }

        /// <summary>
        /// Gets the subscription endpoint from isolated storage.
        /// </summary>
        /// <returns></returns>
        private string GetSubscriptionEndpointFromIsolatedStorage()
        {
            string subscriptionEndpoint = null;

            using (IsolatedStorageFile isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null))
            {
                try
                {
                    using (var iStreamForEndpoint = new IsolatedStorageFileStream(_isolatedStorageSubscriptionEndpointFileName, FileMode.Open, isoStore))
                    {
                        using (var readerForEndpoint = new StreamReader(iStreamForEndpoint))
                        {
                            subscriptionEndpoint = readerForEndpoint.ReadLine();
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                    subscriptionEndpoint = null;
                }
            }
            if (string.IsNullOrEmpty(subscriptionEndpoint))
            {
                subscriptionEndpoint = _defaultSubscriptionEndpointPromptMessage;
            }
            return subscriptionEndpoint;
        }
        
        /// <summary>
        /// Saves the subscription key to isolated storage.
        /// </summary>
        /// <param name="subscriptionKey">The subscription key.</param>
        private void SaveSubscriptionKeyToIsolatedStorage(string subscriptionKey)
        {
            using (IsolatedStorageFile isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null))
            {
                using (var oStream = new IsolatedStorageFileStream(_isolatedStorageSubscriptionKeyFileName, FileMode.Create, isoStore))
                {
                    using (var writer = new StreamWriter(oStream))
                    {
                        writer.WriteLine(subscriptionKey);
                    }
                }
            }
        }

        /// <summary>
        /// Saves the subscription endpoint to isolated storage.
        /// </summary>
        /// <param name="subscriptionEndpoint">The subscription endpoint.</param>
        private void SaveSubscriptionEndpointToIsolatedStorage(string subscriptionEndpoint)
        {
            using (IsolatedStorageFile isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null))
            {
                using (var oStream = new IsolatedStorageFileStream(_isolatedStorageSubscriptionEndpointFileName, FileMode.Create, isoStore))
                {
                    using (var writer = new StreamWriter(oStream))
                    {
                        writer.WriteLine(subscriptionEndpoint);
                    }
                }
            }
        }

        private void SubscriptionKeyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var subscriptionKey = ((TextBox)sender).Text;
            SubscriptionKey = subscriptionKey;
            SaveSubscriptionKeyToIsolatedStorage(subscriptionKey);
        }

        private void SubscriptionEndpointTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var subscriptionEndpoint = ((TextBox)sender).Text;
            SubscriptionEndpoint = subscriptionEndpoint;
            SaveSubscriptionEndpointToIsolatedStorage(subscriptionEndpoint);
        }
    }
}
