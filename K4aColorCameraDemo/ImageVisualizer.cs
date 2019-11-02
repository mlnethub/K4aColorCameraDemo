﻿using K4AdotNet.Sensor;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace K4aColorCameraDemo
{
    /*
     * modified from https://github.com/bibigone/k4a.net/blob/master/K4AdotNet.Samples.Wpf.Viewer/ImageVisualizer.cs
     */
    internal abstract class ImageVisualizer
    {
        public const int DefaultDpi = 96;

        public static ImageVisualizer CreateForColorBgra(Dispatcher dispatcher, int widthPixels, int heightPixels, int dpi = DefaultDpi)
            => new ColorBgraImageVisualizer(dispatcher, widthPixels, heightPixels, dpi);

        public static ImageVisualizer CreateForColorBgra(Dispatcher dispatcher, ColorResolution colorResolution, int dpi = DefaultDpi)
            => new ColorBgraImageVisualizer(dispatcher, colorResolution.WidthPixels(), colorResolution.HeightPixels(), dpi);


        protected ImageVisualizer(Dispatcher dispatcher, ImageFormat format, int widthPixels, int heightPixels, int strideBytes, int dpi)
        {
            if (dispatcher.Thread != Thread.CurrentThread)
            {
                throw new InvalidOperationException(
                    "Call this constructor from UI thread please, because it creates ImageSource object for UI");
            }

            Dispatcher = dispatcher;
            Format = format;
            WidthPixels = widthPixels;
            HeightPixels = heightPixels;
            StrideBytes = strideBytes;

            innerBuffer = new byte[strideBytes * heightPixels];
            writeableBitmap = new WriteableBitmap(widthPixels, heightPixels, dpi, dpi, PixelFormats.Bgra32, null);
        }

        public Dispatcher Dispatcher { get; }
        public ImageFormat Format { get; }
        public int WidthPixels { get; }
        public int HeightPixels { get; }
        public int StrideBytes { get; }

        /// <summary>
        /// Image with visualized frame. You can use this property in WPF controls/windows.
        /// </summary>
        public BitmapSource ImageSource => writeableBitmap;

        /// <summary>
        /// Updates <see cref="ImageSource"/> based on <see cref="Image"/>.
        /// </summary>
        /// <param name="image">Image received from Kinect Sensor SDK. Can be <see langword="null"/>.</param>
        /// <returns><see langword="true"/> - updated, <see langword="false"/> - not updated (frame is not compatible, or old frame).</returns>
        public bool Update(Image image)
        {
            // Is compatible?
            if (image == null
                || image.WidthPixels != WidthPixels || image.HeightPixels != HeightPixels
                || image.Format != Format)
            {
                return false;
            }

            // 1st step: filling of inner buffer
            FillInnerBuffer(image.Buffer, image.StrideBytes, image.SizeBytes);

            // 2nd step: we can update WritableBitmap only from its owner thread (as a rule, UI thread)
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(FillWritableBitmap));

            // Updated
            return true;
        }

        private void FillInnerBuffer(IntPtr srcPtr, int srcStrideBytes, int srcSizeBytes)
        {
            // This method can be called from some background thread,
            // thus use synchronization
            lock (innerBuffer)
            {
                if (srcStrideBytes == StrideBytes && srcSizeBytes == innerBuffer.Length)
                {
                    Marshal.Copy(srcPtr, innerBuffer, 0, innerBuffer.Length);
                }
                else
                {
                    var lineLength = Math.Min(srcStrideBytes, StrideBytes);
                    var dstOffset = 0;
                    for (var y = 0; y < HeightPixels; y++)
                    {
                        Marshal.Copy(srcPtr, innerBuffer, dstOffset, lineLength);
                        srcPtr += srcSizeBytes;
                        dstOffset += StrideBytes;
                    }
                }
            }
        }

        private void FillWritableBitmap()
        {
            writeableBitmap.Lock();
            try
            {
                var backBuffer = writeableBitmap.BackBuffer;
                var backBufferStride = writeableBitmap.BackBufferStride;
                lock (innerBuffer)
                {
                    // We use parallelism here to speed up
                    Parallel.For(0, HeightPixels, y => FillWritableBitmapLine(y, backBuffer, backBufferStride));
                }

                // Inform UI infrastructure that we have updated content of image
                writeableBitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, WidthPixels, HeightPixels));
            }
            finally
            {
                writeableBitmap.Unlock();
            }
        }

        protected abstract void FillWritableBitmapLine(int y, IntPtr backBuffer, int backBufferStride);

        protected readonly byte[] innerBuffer;
        protected readonly WriteableBitmap writeableBitmap;
        

        private sealed class ColorBgraImageVisualizer : ImageVisualizer
        {
            public ColorBgraImageVisualizer(Dispatcher dispatcher, int widthPixels, int heightPixels, int dpi)
                : base(dispatcher, ImageFormat.ColorBgra32, widthPixels, heightPixels, ImageFormat.ColorBgra32.StrideBytes(widthPixels), dpi)
            { }

            protected override void FillWritableBitmapLine(int y, IntPtr backBuffer, int backBufferStride)
            {
                var lineLength = Math.Min(backBufferStride, StrideBytes);
                Marshal.Copy(innerBuffer, y * StrideBytes, backBuffer + backBufferStride * y, lineLength);
            }
        }
    }
}
