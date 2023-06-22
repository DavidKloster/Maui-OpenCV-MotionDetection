
using Android.Animation;
using Camera.MAUI;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Microsoft.Maui.Controls;
using SkiaSharp;
using System.Collections.Concurrent;

namespace MauiApp3;

public partial class MainPage : ContentPage
{
    private Thread thread;
    private volatile bool isCapturing;
    private VideoCapture capture;

    private ConcurrentQueue<SKBitmap> _bitmapQueue = new ConcurrentQueue<SKBitmap>();
    private SKBitmap _bitmap;

    private static object lockObject = new object();
    private Mat prevImage;
    public MainPage()
    {
        InitializeComponent();
        cameraView.CamerasLoaded += CameraView_CamerasLoaded;
#if ANDROID
        CvInvokeAndroid.Init();
#endif
        this.Disappearing += OnDisappearing;


    }
    private void OnDisappearing(object sender, EventArgs e)
    {
        Destroy();
    }
    private void Destroy()
    {
        lock (lockObject)
        {

            ClearQueue();

            if (_bitmap != null)
            {
                _bitmap.Dispose();
                _bitmap = null;
            }
        }
    }

    private void ClearQueue()
    {
        _bitmapQueue.Clear();
    }
    private async void StartTimer()
    {
        await Task.Run(async () =>
        {
            Mat prevImage = null;
            while (true)
            {
                ImageSource imageSource = cameraView.GetSnapShot(Camera.MAUI.ImageFormat.PNG);
                if (imageSource == null) { await Task.Delay(1); continue; };
                byte[] strm = await ConvertImageSourceToBytesAsync(imageSource);

                Mat image = new();
                Mat Rgb = new();
                Mat gray = new();
                Mat processed  =new();
                CvInvoke.Imdecode(strm, ImreadModes.Unchanged, image);
                CvInvoke.CvtColor(image, Rgb, ColorConversion.Bgr2Rgb);
                CvInvoke.CvtColor(Rgb, gray, ColorConversion.Bgr2Gray);
                CvInvoke.GaussianBlur(gray,processed,new(5,5),0);

                if (prevImage == null)
                {
                    prevImage = processed;
                    continue;
                }
                Mat dilateFrame = new Mat();
                Mat kernel = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new(3, 3), new(1, 1));
                CvInvoke.Dilate(processed, dilateFrame, kernel, new(1, 1), 1, BorderType.Reflect, default);

                Mat diffFrame = new();
                Mat threshFrame = new();

                CvInvoke.AbsDiff(prevImage, dilateFrame, diffFrame);
                CvInvoke.Threshold(diffFrame, threshFrame, 20, 255, ThresholdType.BinaryInv);

                VectorOfVectorOfPoint contours = new();
                Mat hierarchy = new Mat();
                CvInvoke.FindContours(threshFrame, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);

     
               Mat cont_frame = new();
                // CvInvoke.DrawContours(image,contours,-1,new MCvScalar(0,255,0),2,LineType.AntiAlias);

                for (int i = 0; i < contours.Size; i++)
                {
                    if (CvInvoke.ContourArea(contours[i]) < 400) continue;
                    var rect = CvInvoke.BoundingRectangle(contours[i]);
                    CvInvoke.Rectangle(image, rect, new MCvScalar(0, 255, 0), 2);

                }


                SKBitmap bitmap = new SKBitmap(image.Cols, image.Rows, SKColorType.Bgra8888, SKAlphaType.Premul);
                bitmap.SetPixels(image.DataPointer);

 

                if (_bitmapQueue.Count == 2) ClearQueue();
                _bitmapQueue.Enqueue(bitmap);

                Device.InvokeOnMainThreadAsync(() =>
                {
                    canvasView.InvalidateSurface();
                });
                
         

         
            }
        });

    }
    public async Task<byte[]> ConvertImageSourceToBytesAsync(ImageSource imageSource)
    {
        Stream stream = await ((StreamImageSource)imageSource).Stream(CancellationToken.None);
        byte[] bytesAvailable = new byte[stream.Length];
        stream.Read(bytesAvailable, 0, bytesAvailable.Length);

        return bytesAvailable;
    }

    public static byte[] ReadFully(Stream input)
    {
        byte[] buffer = new byte[16 * 1024];
        using (MemoryStream ms = new MemoryStream())
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }
    }
    private void CameraView_CamerasLoaded(object sender, EventArgs e)
    {
        if (cameraView.NumCamerasDetected > 0)
        {
            if (cameraView.NumMicrophonesDetected > 0)
                cameraView.Microphone = cameraView.Microphones.First();
            cameraView.Camera = cameraView.Cameras.First();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (await cameraView.StartCameraAsync() == CameraResult.Success)
                {
                    StartTimer();
                }
            });

           
        }
    }

    private void canvasView_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs args)
    {
        lock (lockObject)
        {
      

            SKSurface surface = args.Surface;
            SKCanvas canvas = surface.Canvas;

            canvas.Clear();

            _bitmapQueue.TryDequeue(out _bitmap);

            if (_bitmap != null)
            {
                try
                {
                    canvas.DrawBitmap(_bitmap, new SKPoint(0, 0));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
                finally
                {
                    _bitmap.Dispose();
                    _bitmap = null;
                }
            }
        }
    }
}