using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
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

        public MainWindow()
        {
            InitializeComponent();

            _faceClient = new FaceServiceClient("848829ca90474c78bd9e57c0e52ad694", "https://westus.api.cognitive.microsoft.com/face/v1.0");

            _grabber = new FrameGrabber<LiveCameraResult>
            {
                AnalysisFunction = IdentifyFaceFunction
            };
            _grabber.TriggerAnalysisOnInterval(new TimeSpan(0, 0, 2));
        }

        private async void OnWindowLoad(object sender, RoutedEventArgs eventArgs)
        {
            _persons = await _faceClient.ListPersonsAsync(GroupName);

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

    }
}
