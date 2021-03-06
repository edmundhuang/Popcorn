﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Akavache;
using CefSharp;
using GalaSoft.MvvmLight.Messaging;
using GalaSoft.MvvmLight.Threading;
using NLog;
using Popcorn.Helpers;
using Popcorn.Messaging;
using Popcorn.Utils;
using Popcorn.Utils.Exceptions;
using Popcorn.ViewModels.Windows;
using Popcorn.Windows;
using Squirrel;
using WPFLocalizeExtension.Engine;

namespace Popcorn
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        /// <summary>
        /// Logger of the class
        /// </summary>
        private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Splash screen dispatcher
        /// </summary>
        private Dispatcher _splashScreenDispatcher;

        /// <summary>
        /// Watcher
        /// </summary>
        private Stopwatch WatchStart { get; set; }
    
        /// <summary>
        /// If first run
        /// </summary>
        private static bool _firstRun;

        /// <summary>
        /// Loading semaphore
        /// </summary>
        private readonly SemaphoreSlim _windowLoadedSemaphore = new SemaphoreSlim(1, 1);

        private static void OnFirstRun()
        {
            Logger.Info("Triggered OnFirstRun...");
            // TODO: Must complete welcome.md page before activate this feature
            //_firstRun = true;
        }

        private static void OnAppUninstall(Version version)
        {
            Logger.Info("Triggered OnAppUninstall...");

            using (var manager = new UpdateManager(Constants.GithubRepository))
            {
                manager.RemoveShortcutsForExecutable("Popcorn.exe", ShortcutLocation.Desktop);
                manager.RemoveShortcutsForExecutable("Popcorn.exe", ShortcutLocation.StartMenu);
                manager.RemoveShortcutsForExecutable("Popcorn.exe", ShortcutLocation.AppRoot);

                manager.RemoveUninstallerRegistryEntry();
            }
        }

        private static void OnAppUpdate(Version version)
        {
            Logger.Info("Triggered OnAppUpdate...");

            using (var manager = new UpdateManager(Constants.GithubRepository))
            {
                manager.CreateShortcutsForExecutable("Popcorn.exe", ShortcutLocation.Desktop, true);
                manager.CreateShortcutsForExecutable("Popcorn.exe", ShortcutLocation.StartMenu, true);
                manager.CreateShortcutsForExecutable("Popcorn.exe", ShortcutLocation.AppRoot, true);

                manager.RemoveUninstallerRegistryEntry();
                manager.CreateUninstallerRegistryEntry();
            }
        }

        private static void OnInitialInstall(Version version)
        {
            Logger.Info("Triggered OnInitialInstall...");

            using (var manager = new UpdateManager(Constants.GithubRepository))
            {
                manager.CreateShortcutForThisExe();

                manager.CreateShortcutsForExecutable("Popcorn.exe", ShortcutLocation.Desktop, false);
                manager.CreateShortcutsForExecutable("Popcorn.exe", ShortcutLocation.StartMenu, false);
                manager.CreateShortcutsForExecutable("Popcorn.exe", ShortcutLocation.AppRoot, false);

                manager.CreateUninstallerRegistryEntry();
            }
        }

        static App()
        {
            DispatcherHelper.Initialize();
            LocalizeDictionary.Instance.SetCurrentThreadCulture = true;
            BlobCache.ApplicationName = "Popcorn";
            Cef.Initialize();
        }

        /// <summary>
        /// Initializes a new instance of the App class.
        /// </summary>
        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += AppDispatcherUnhandledException;
            Dispatcher.UnhandledException += Dispatcher_UnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        /// <summary>
        /// On startup, register synchronization context
        /// </summary>
        /// <param name="e"></param>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            WatchStart = Stopwatch.StartNew();
            Logger.Info(
                "Popcorn starting...");
            AsyncSynchronizationContext.Register();
            try
            {
                SquirrelAwareApp.HandleEvents(
                    onInitialInstall: OnInitialInstall,
                    onAppUpdate: OnAppUpdate,
                    onAppUninstall: OnAppUninstall,
                    onFirstRun: OnFirstRun);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        /// <summary>
        /// Observe unhandled exceptions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            CurrentDomainUnhandledException(sender, new UnhandledExceptionEventArgs(e.Exception, false));
        }

        /// <summary>
        /// Handle unhandled expceptions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Dispatcher_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            CurrentDomainUnhandledException(sender, new UnhandledExceptionEventArgs(e.Exception, false));
        }

        /// <summary>
        /// When an unhandled exception has been thrown, handle it
        /// </summary>
        /// <param name="sender"><see cref="App"/> instance</param>
        /// <param name="e">DispatcherUnhandledExceptionEventArgs args</param>
        private void AppDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            CurrentDomainUnhandledException(sender, new UnhandledExceptionEventArgs(e.Exception, false));
        }

        /// <summary>
        /// When an unhandled exception domain has been thrown, handle it
        /// </summary>
        /// <param name="sender"><see cref="App"/> instance</param>
        /// <param name="e">UnhandledExceptionEventArgs args</param>
        private static void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Fatal(ex);
                Messenger.Default.Send(
                    new UnhandledExceptionMessage(
                        new PopcornException(LocalizationProviderHelper.GetLocalizedValue<string>("FatalError"))));
            }
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            var splashScreenThread = new Thread(() =>
            {
                var splashScreen = new Windows.SplashScreen();
                _splashScreenDispatcher = splashScreen.Dispatcher;
                _splashScreenDispatcher.ShutdownStarted += (o, args) => splashScreen.Close();
                splashScreen.Show();
                Dispatcher.Run();
            });

            splashScreenThread.SetApartmentState(ApartmentState.STA);
            splashScreenThread.Start();

            var mainWindow = new MainWindow();
            mainWindow.Topmost = true;
            MainWindow = mainWindow;
            mainWindow.Loaded += async (sender2, e2) =>
                await mainWindow.Dispatcher.InvokeAsync(async () =>
                {
                    await _windowLoadedSemaphore.WaitAsync();
                    if (!WatchStart.IsRunning)
                        return;
                    _splashScreenDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    mainWindow.Activate();
                    var vm = mainWindow.DataContext as WindowViewModel;
                    if (vm != null)
                    {
                        vm.InitializeAsyncCommand.Execute(null);
                        if(_firstRun)
                            vm.OpenWelcomeCommand.Execute(null);
                    }
                    WatchStart.Stop();
                    var elapsedStartMs = WatchStart.ElapsedMilliseconds;
                    Logger.Info(
                        $"Popcorn started in {elapsedStartMs} milliseconds.");
                    _windowLoadedSemaphore.Release();
                });

            mainWindow.Show();
        }
    }
}