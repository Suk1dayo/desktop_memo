using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DesktopMemo
{
    public static class Program
    {
        private const string MutexName = "Local\\DesktopMemo_SingleInstance_v1";
        private const string WakeupEventName = "Local\\DesktopMemo_Wakeup_v1";

        [STAThread]
        public static int Main(string[] args)
        {
            // Ensure TLS 1.2 / 1.3 for DeepSeek / OpenAI HTTPS requests on .NET 4.8
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;
            Win32Native.EnablePerMonitorDpiAwareness();

            string selfTestReportPath = null;
            bool runSelfTest = false;
            bool startedFromAutoStart = false;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i] ?? "";
                    if (arg == "--self-test")
                    {
                        runSelfTest = true;
                    }
                    else if (arg.StartsWith("--self-test=", StringComparison.OrdinalIgnoreCase))
                    {
                        runSelfTest = true;
                        selfTestReportPath = arg.Substring("--self-test=".Length).Trim('"');
                    }
                    else if (arg == "--autostart")
                    {
                        startedFromAutoStart = true;
                    }
                }
            }

            if (runSelfTest)
            {
                return Tests.RunAll(selfTestReportPath);
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Signal existing instance to bring windows forward
                    try
                    {
                        using (EventWaitHandle wakeup = EventWaitHandle.OpenExisting(WakeupEventName))
                        {
                            wakeup.Set();
                        }
                    }
                    catch { }
                    return 0;
                }

                var storage = new StorageService(null);
                EventWaitHandle wakeupHandle = new EventWaitHandle(false, EventResetMode.AutoReset, WakeupEventName);
                TrayAndAppController controller = null;

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    storage.LogError("DispatcherUnhandledException", e.Exception);
                    MessageBox.Show("程序发生未处理异常（已记录到 data\\error.log）：\n" + e.Exception.Message,
                        "桌面备忘录", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };

                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    storage.LogError("AppDomain.UnhandledException", e.ExceptionObject as Exception);
                };

                TaskScheduler.UnobservedTaskException += delegate(object s, UnobservedTaskExceptionEventArgs e)
                {
                    storage.LogError("UnobservedTaskException", e.Exception);
                    e.SetObserved();
                };

                controller = new TrayAndAppController(storage);

                var listenerThread = new Thread(delegate()
                {
                    while (true)
                    {
                        try
                        {
                            wakeupHandle.WaitOne();
                            if (app.Dispatcher.HasShutdownStarted) break;
                            app.Dispatcher.BeginInvoke(new Action(delegate
                            {
                                if (controller != null) controller.WakeupFromSecondInstance();
                            }));
                        }
                        catch
                        {
                            break;
                        }
                    }
                });
                listenerThread.IsBackground = true;
                listenerThread.Start();

                controller.Start(startedFromAutoStart);
                app.Exit += delegate
                {
                    try { wakeupHandle.Set(); wakeupHandle.Dispose(); } catch { }
                    if (controller != null) controller.Shutdown();
                };

                return app.Run();
            }
        }
    }
}
