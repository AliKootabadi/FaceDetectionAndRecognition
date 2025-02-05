﻿using System;
using System.Collections.Generic;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.IO;
using System.Windows;
using System.ComponentModel;
using System.Timers;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
using Emgu.CV.Face;
using Emgu.CV.Util;
using Microsoft.Win32;

namespace FaceDetectionAndRecognition
{
    public partial class WFFaceRecognition : Window, INotifyPropertyChanged
    {
        #region ' Properties '
        public event PropertyChangedEventHandler PropertyChanged;
        private VideoCapture videoCapture;
        private CascadeClassifier haarCascade;
        private Image<Bgr, Byte> bgrFrame = null;
        private Image<Gray, Byte> detectedFace = null;
        private List<FaceData> faceList = new List<FaceData>();
        private VectorOfMat imageList = new VectorOfMat();
        private List<string> nameList = new List<string>();
        private VectorOfInt labelList = new VectorOfInt();

        private EigenFaceRecognizer recognizer;
        private Timer captureTimer;

        private string faceName;
        public string FaceName
        {
            get { return faceName; }
            set
            {
                faceName = value.ToUpper();
                lblFaceName.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() => { lblFaceName.Content = faceName; }));
                NotifyPropertyChanged();
            }
        }

        private Bitmap cameraCapture;
        public Bitmap CameraCapture
        {
            get { return cameraCapture; }
            set
            {
                cameraCapture = value;
                imgCamera.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() => { imgCamera.Source = BitmapToImageSource(cameraCapture); }));
                NotifyPropertyChanged();
            }
        }

        private Bitmap cameraCaptureFace;
        public Bitmap CameraCaptureFace
        {
            get { return cameraCaptureFace; }
            set
            {
                cameraCaptureFace = value;
                imgDetectFace.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() => { imgDetectFace.Source = BitmapToImageSource(cameraCaptureFace); }));
                // imgCamera.Source = BitmapToImageSource(cameraCapture);
                NotifyPropertyChanged();
            }
        }

        public WFFaceRecognition()
        {
            InitializeComponent();
            captureTimer = new Timer()
            {
                Interval = Config.TimerResponseValue
            };
            captureTimer.Elapsed += CaptureTimer_Elapsed;
        }
        #endregion

        #region ' Event '
        protected virtual void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            GetFacesList();
            videoCapture = new VideoCapture(Config.ActiveCameraIndex);
            videoCapture.SetCaptureProperty(CapProp.Fps, 30);
            videoCapture.SetCaptureProperty(CapProp.FrameHeight, 450);
            videoCapture.SetCaptureProperty(CapProp.FrameWidth, 370);
            captureTimer.Start();
        }
        private void CaptureTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            ProcessFrame();
        }
        private void NewFaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (detectedFace == null)
            {
                MessageBox.Show("No face detected.");
                return;
            }

            detectedFace = detectedFace.Resize(100, 100, Inter.Cubic);
            detectedFace.Save(Config.FacePhotosPath + "face" + (faceList.Count + 1) + Config.ImageFileExtension);
            StreamWriter writer = new StreamWriter(Config.FaceListTextFile, true);
            string personName = Microsoft.VisualBasic.Interaction.InputBox("Your Name");
            writer.WriteLine(String.Format("face{0}:{1}", (faceList.Count + 1), personName));
            writer.Close();
            GetFacesList();
            MessageBox.Show("Successful.");
        }
        private void OpenVideoFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog();
            if (openDialog.ShowDialog().Value == true)
            {
                captureTimer.Stop();
                videoCapture.Dispose();

                videoCapture = new VideoCapture(openDialog.FileName);
                captureTimer.Start();
                this.Title = openDialog.FileName;
                return;
            }
        }
        #endregion

        #region ' Method '
        public void GetFacesList()
        {
            haarCascade = new CascadeClassifier(Config.HaarCascadePath);
            faceList.Clear();
            string line;
            FaceData faceInstance = null;

            if (!Directory.Exists(Config.FacePhotosPath))
                Directory.CreateDirectory(Config.FacePhotosPath);
            if (!File.Exists(Config.FaceListTextFile))
            {
                String dirName = Path.GetDirectoryName(Config.FaceListTextFile);
                Directory.CreateDirectory(dirName);
                File.Create(Config.FaceListTextFile).Close();
            }

            StreamReader reader = new StreamReader(Config.FaceListTextFile);
            int i = 0;
            while ((line = reader.ReadLine()) != null)
            {
                string[] lineParts = line.Split(':');
                faceInstance = new FaceData();
                faceInstance.FaceImage = new Image<Gray, byte>(Config.FacePhotosPath + lineParts[0] + Config.ImageFileExtension);
                faceInstance.PersonName = lineParts[1];
                faceList.Add(faceInstance);
            }
            foreach (var face in faceList)
            {
                imageList.Push(face.FaceImage.Mat);
                nameList.Add(face.PersonName);
                labelList.Push(new[] { i++ });
            }
            reader.Close();

            // Train recogniser
            if (imageList.Size > 0)
            {
                recognizer = new EigenFaceRecognizer(imageList.Size);
                recognizer.Train(imageList, labelList);
            }
        }
        private void ProcessFrame()
        {
            bgrFrame = videoCapture.QueryFrame().ToImage<Bgr, Byte>();

            if (bgrFrame != null)
            {
                try
                {
                    Image<Gray, byte> grayframe = bgrFrame.Convert<Gray, byte>();

                    Rectangle[] faces = haarCascade.DetectMultiScale(grayframe, 1.2, 10, new System.Drawing.Size(50, 50), new System.Drawing.Size(200, 200));

                    //detect face
                    FaceName = "No face detected";
                    foreach (var face in faces)
                    {
                        bgrFrame.Draw(face, new Bgr(255, 255, 0), 2);
                        detectedFace = bgrFrame.Copy(face).Convert<Gray, byte>();
                        FaceRecognition();
                        break;
                    }
                    CameraCapture = bgrFrame.ToBitmap();
                }
                catch
                {}
            }
        }
        private void FaceRecognition()
        {
            if (imageList.Size != 0)
            {
                //Eigen Face Algorithm
                FaceRecognizer.PredictionResult result = recognizer.Predict(detectedFace.Resize(100, 100, Inter.Cubic));
                FaceName = nameList[result.Label];
                CameraCaptureFace = detectedFace.ToBitmap();
            }
            else
            {
                FaceName = "Please Add Face";
            }
        }
        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                BitmapImage bitmapimage = new BitmapImage();
                bitmapimage.BeginInit();
                bitmapimage.StreamSource = memory;
                bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapimage.EndInit();

                return bitmapimage;
            }
        }
        #endregion
    }
}
