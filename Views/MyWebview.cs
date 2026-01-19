using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.WindowManagement;

namespace PayrollManager.UI.Views
{
    public class MyWebview : WebView2
    {
        public MyWebview()
        {
            this.CoreWebView2Initialized += MyWebview_CoreWebView2Initialized;
        }

        /// <summary>
        /// 所以订阅事件必须在initialized后订阅有效
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MyWebview_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
        {
            this.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            this.CoreWebView2.ContainsFullScreenElementChanged += CoreWebView2_ContainsFullScreenElementChanged;
        }

        /// <summary>
        /// 解决webview2视频全屏问题
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void CoreWebView2_ContainsFullScreenElementChanged(Microsoft.Web.WebView2.Core.CoreWebView2 sender, object args)
        {
           if (sender.ContainsFullScreenElement)
            {
                Utils.Helper.MainWindow.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }else
            {
                Utils.Helper.MainWindow.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            }
        }


        /// <summary>
        /// 重写newwindow 事件，阻止网页在新窗口弹出
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void CoreWebView2_NewWindowRequested(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs args)
        {
            this.Source=new Uri(args.Uri);
            args.Handled = true;
        }
    }
}
