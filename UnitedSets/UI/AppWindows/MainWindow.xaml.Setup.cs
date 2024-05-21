﻿using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using UnitedSets.Classes;
using UnitedSets.Classes.Tabs;
using UnitedSets.Helpers;
using WinRT.Interop;
using WinUIEx.Messaging;
using Window = WinWrapper.Windowing.Window;
using Get.EasyCSharp;
using Microsoft.UI.Windowing;
using Keyboard = WinWrapper.Input.Keyboard;
using Windows.ApplicationModel;
using Windows.Foundation;
using System.Runtime.CompilerServices;
using UnitedSets.Mvvm.Services;
using Icon = WinWrapper.Icon;
using UnitedSets.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.IO;
using System.Threading.Tasks;
using WinUIEx;

namespace UnitedSets.UI.AppWindows;

partial class MainWindow
{
    // To Merge:

    public MainWindow(PreservedTabDataService persistantService)
    {
        // To Merge:
        this.persistantService = persistantService;

        if (cfg.Design.Theme != null && cfg.Design.Theme != ElementTheme.Default && USConfig.FLAGS_THEME_CHOICE_ENABLED)
        {
            (this.Content as FrameworkElement).RequestedTheme = cfg.Design.Theme.Value;
            ///not sure why settng the requested theme doesnt seem to work
            //MainAreaBorder.RequestedTheme = this.RootGrid.RequestedTheme = swapChainPanel.RequestedTheme = cfg.Design.Theme.Value;
        }

        ui_configs = new() { TitleUpdate = UpdateTitle }; //, swapChain = this.swapChainPanel
        persistantService.init(this, ui_configs);

        //if (cfg.Design.UseDXBorderTransparency == true)
        //    TransparentSetup();

        InitializeComponent();
        SetupBasicWindow();
        
        //TransparentMode = FeatureFlags.UseTransparentWindow;
        //if (TransparentMode)
        //    SetupTransparent(out trans_mgr);

        //void UpdateBackdrop(USBackdrop x)
        //{
        //    //var IsTransparent = x is USBackdrop.Transparent;
        //    //WindowBorderOnTransparent.Visibility = IsTransparent ? Visibility.Visible : Visibility.Collapsed;
        //    //TransparentMode = IsTransparent;
        //    SystemBackdrop = x.GetSystemBackdrop();
        //}
        //Settings.BackdropMode.Updated += UpdateBackdrop;
        //UpdateBackdrop(Settings.BackdropMode.Value);
        ((OverlappedPresenter)AppWindow.Presenter).SetBorderAndTitleBar(true, true);

        SetupNative(out Win32Window, out WindowMessageMonitor);

        RegisterWindow();

        SetupEvent();

        SetupUIThreadLoopTimer(out timer);

        // SetupTaskbarMode();

        // Implementation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void RegisterWindow()
        {
            TabBase.MainWindows.Add(Win32Window);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetupBasicWindow()
        {
            Title = "UnitedSets";
            MinWidth = 100;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomDragRegion);
        }
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //void SetupTransparent(out TransparentWindowManager trans_mgr)
        //{
        //    WindowBorderOnTransparent.Visibility = Visibility.Visible;
        //    MainAreaBorder.Margin = new(8, 0, 8, 8);
        //    var presenter = (OverlappedPresenter)AppWindow.Presenter;
        //    //RootGrid.Children.Insert(0, border);
        //    //trans_mgr = new(this, swapChainPanel, FeatureFlags.EntireWindowDraggable);
        //    //trans_mgr.AfterInitialize();
        //    trans_mgr = null!;
        //    ui_configs.WindowBorder = new Border() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        //    ui_configs.WindowBorder.BorderBrush = new LinearGradientBrush(new GradientStopCollection { new GradientStop { Offset = 1 }, new GradientStop { Offset = 0 } }, 45);
        //    //ui_configs.WindowBorder.RequestedTheme = MainAreaBorder.RequestedTheme;
        //    Grid.SetColumnSpan(ui_configs.WindowBorder, 50);
        //    Grid.SetRowSpan(ui_configs.WindowBorder, 50);
        //    Canvas.SetZIndex(ui_configs.WindowBorder, -5);
        //    ui_configs.MainAreaBorder = MainAreaBorder;
        //    RootGrid.Children.Insert(0, ui_configs.WindowBorder);
        //    persistantService.SetPrimaryDesignProperties();
        //    trans_mgr = new(this, swapChainPanel, cfg.DragAnywhere == true);
        //    trans_mgr.AfterInitialize();
        //}
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetupEvent()
        {
            AppWindow.Closing += OnWindowClosing;
            // Activated += FirstRun;
            WindowMessageMonitor.WindowMessageReceived += OnWindowMessageReceived;
            TabBase.OnUpdateStatusLoopComplete += OnDifferentThreadLoop;
            Cell.ValidDrop += CellWindowDropped;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetupNative(out Window WindowEx, out WindowMessageMonitor WindowMessageMonitor)
        {
            WindowEx = Window.FromWindowHandle(WindowNative.GetWindowHandle(this));
            WindowMessageMonitor = new WindowMessageMonitor(WindowEx);
        }
        
    }

    public class CfgElements
    {
        public Border? WindowBorder;
        public Border? MainAreaBorder;
        public SwapChainPanel swapChain;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Action TitleUpdate { init; get; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    }

    private CfgElements ui_configs;

    private void UpdateTitle()
    {
        var prefix = cfg.TitlePrefix + " ";
        if (TabView.SelectedIndex != -1)
            prefix += $"{Tabs[TabView.SelectedIndex].Title} (+{Tabs.Count - 1} Tabs) - ";

        Title = $"{prefix.TrimStart()}United Sets";

    }

    public USConfig cfg => Settings.cfg;

    private PreservedTabDataService persistantService;
    [Event(typeof(TypedEventHandler<object, WindowActivatedEventArgs>))]
    void FirstRun()
    {
        Activated -= FirstRun;
        var icoFile = Path.IsPathRooted(cfg.TaskbarIco) ? cfg.TaskbarIco : Path.Combine(USConfig.RootLocation, cfg.TaskbarIco!);
        var icon = Icon.Load(icoFile);
        Win32Window.SmallIcon = Win32Window.LargeIcon = icon;

        if (Keyboard.IsShiftDown)
            Win32Window.SetAppId($"UnitedSets {Win32Window.Handle}");
        HandleCLICmds();
    }
    async void HandleCLICmds()
    {
        var toAdd = CLI.GetArrVal("add-window-by-exe");
        var editLastAddedWindow = CLI.GetFlag("edit-last-added");
        //LeftFlyout.NoAutoClose = CLI.GetFlag("edit-no-autoclose");
        var profile = CLI.GetVal("profile");
        if (!String.IsNullOrWhiteSpace(profile))
        {
            if (Path.HasExtension(profile) == false)
                profile += ".json";
            if (!File.Exists(profile) && !Path.IsPathRooted(profile))
                profile = Path.Combine(USConfig.BaseProfileFolder, profile);
            if (File.Exists(profile))
            {
                await Task.Delay(1500);
                await persistantService.ImportSettings(profile);
            }


        }

        foreach (var itm in toAdd)
        {
            var procs = System.Diagnostics.Process.GetProcesses().Where(p => p.ProcessName.Equals(itm, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var proc in procs)
            {
                if (!proc.HasExited)
                    AddTab(WinWrapper.Windowing.Window.FromWindowHandle(proc.MainWindowHandle));
            }
        }
        if (editLastAddedWindow && Tabs.Count > 0)
            Tabs.Last().TabDoubleTapped(this, new Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs());


    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private partial void SetupUIThreadLoopTimer(out DispatcherQueueTimer timer)
    {
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.Tick += OnUIThreadTimerLoop;
        timer.Start();
    }
}
