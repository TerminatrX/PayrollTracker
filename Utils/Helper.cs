using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace PayrollManager.UI.Utils
{
    public static class Helper
    { 
        public static Window? MainWindow { get; set; }
        /// <summary>
        /// 封装openfile方法
        /// </summary>
        /// <param name="window">打开filepicker的窗口</param>
        /// <param name="filter">打开文件的过滤器</param>
        /// <returns></returns>
        public static FileOpenPicker FileOpenPicker(Window window,string filter="*") {

           
            FileOpenPicker openPicker = new FileOpenPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);
            openPicker.FileTypeFilter.Add(filter);
            return openPicker;
        }

        public static void ChangeFullScreen(Window window)
        {
            if (window.AppWindow.Presenter.Kind== AppWindowPresenterKind.FullScreen)
            {
                window.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            }
            else
            {

                window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }

        }
        /// <summary>
        /// 判断文本文件的编码
        /// </summary>
        /// <param name="FilePath">文件路径</param>
        /// <returns></returns>
        public static Encoding DetectEncoding(string FilePath)
        {
           var result=  UtfUnknown.CharsetDetector.DetectFromFile(FilePath);
           return result.Detected.Encoding;

        }
    }
}
