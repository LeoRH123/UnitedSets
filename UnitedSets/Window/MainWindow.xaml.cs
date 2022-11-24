﻿using EasyCSharp;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using WinRT.Interop;
using WinUIEx;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using System;
using WindowRelative = WinWrapper.WindowRelative;
using WindowEx = WinWrapper.Window;
using Cursor = WinWrapper.Cursor;
using Keyboard = WinWrapper.Keyboard;
using UnitedSets.Classes;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using UnitedSets.Services;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Diagnostics;
using WinUIEx.Messaging;
using Microsoft.UI.Dispatching;
using System.Threading;
using System.IO;
using WinWrapper;
using System.Text.RegularExpressions;
using Windows.Foundation;
namespace UnitedSets;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public const string UnitedSetsTabWindowDragProperty = "UnitedSetsTabWindow";
    public static readonly uint UnitedSetCommunicationChangeWindowOwnership
        = PInvoke.RegisterWindowMessage(nameof(UnitedSetCommunicationChangeWindowOwnership));

    public SettingsService Settings = App.Current.Services.GetService<SettingsService>() ?? throw new InvalidOperationException();
    public ObservableCollection<TabBase> Tabs { get; } = new();
    readonly WindowEx WindowEx;
    bool _HasOwner = false;
    Visibility SettingsButtonVisibility => HasOwner ? Visibility.Collapsed : Visibility.Visible;
    public bool HasOwner
    {
        get => WindowEx.Owner.IsValid;
    }
    DispatcherQueueTimer timer;
    WindowMessageMonitor WindowMessageMonitor;
    public System.Drawing.Rectangle CacheMiddleAreaBounds { get; set; }

    TabBase? SelectedTabCache;
    public MainWindow()
    {
        Title = "UnitedSets";
        InitializeComponent();
        MinWidth = 100;
        WindowEx = WindowEx.FromWindowHandle(WindowNative.GetWindowHandle(this));
        WindowMessageMonitor = new WindowMessageMonitor(WindowEx);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(CustomDragRegion);

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);

        TabBase.MainWindows.Add(WindowEx);
        AppWindow.Closing += OnWindowClosing;
        Activated += FirstRun;
        SizeChanged += OnMainWindowResize;
        CustomDragRegionUpdator.EffectiveViewportChanged += OnCustomDragRegionUpdatorCalled;
        WindowMessageMonitor.WindowMessageReceived += OnWindowMessageReceived;
        TabBase.OnUpdateStatusLoopComplete += OnLoopCalled;
        timer.Tick += OnTimerLoopTick;
        timer.Start();
        
    }

    [Event(typeof(TypedEventHandler<object, WindowActivatedEventArgs>))]
    void FirstRun()
    {
        Activated -= FirstRun;
        var icon = PInvoke.LoadImage(
            hInst: null,
            name: $@"{Package.Current.InstalledLocation.Path}\Assets\UnitedSets.ico",
            type: GDI_IMAGE_TYPE.IMAGE_ICON,
        cx: 0,
        cy: 0,
            fuLoad: IMAGE_FLAGS.LR_LOADFROMFILE | IMAGE_FLAGS.LR_DEFAULTSIZE | IMAGE_FLAGS.LR_SHARED
        );
        bool success = false;
        icon.DangerousAddRef(ref success);
        PInvoke.SendMessage(WindowEx.Handle, PInvoke.WM_SETICON, 1, icon.DangerousGetHandle());
        PInvoke.SendMessage(WindowEx.Handle, PInvoke.WM_SETICON, 0, icon.DangerousGetHandle());

        if (Keyboard.IsShiftDown)
            WindowEx.SetAppId($"UnitedSets {WindowEx.Handle}");
    }

    [Event(typeof(TypedEventHandler<FrameworkElement, EffectiveViewportChangedEventArgs>))]
    void OnCustomDragRegionUpdatorCalled()
    {
        CustomDragRegion.Width = CustomDragRegionUpdator.ActualWidth - 10;
        CustomDragRegion.Height = CustomDragRegionUpdator.ActualHeight;
    }

    [Event(typeof(TypedEventHandler<object, WindowSizeChangedEventArgs>))]
    void OnMainWindowResize()
    {
        TabView.MaxWidth = RootGrid.ActualWidth - 140;
    }

    [Event(typeof(EventHandler<WindowMessageEventArgs>))]
    void OnWindowMessageReceived(WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == UnitedSetCommunicationChangeWindowOwnership)
        {
            var winPtr = e.Message.LParam;
            if (Tabs.FirstOrDefault(x => x.Windows.Any(y => y == winPtr)) is TabBase Tab)
            {
                Tab.DetachAndDispose(false);
                e.Result = 1;
            }
            else e.Result = 0;
        }
    }

    readonly AddTabFlyout AddTabFlyout = new();

    [Event(typeof(TypedEventHandler<TabView, object>))]
    async void OnAddTabButtonClick()
    {
        if (Keyboard.IsShiftDown)
        {
            var newTab = new CellTab(this);
            Tabs.Add(newTab);
            TabView.SelectedItem = newTab;
        } else
        {
            WindowEx.Minimize();
            await AddTabFlyout.ShowAsync();
            WindowEx.Restore();
            var result = AddTabFlyout.Result;
            AddTab(result);
        }
    }

    void AddTab(WindowEx newWindow, int? index = null)
    {
        if (!newWindow.IsValid)
            return;
        newWindow = newWindow.Root;
        if (newWindow.Handle == IntPtr.Zero)
            return;
        if (newWindow.Handle == AddTabFlyout.GetWindowHandle())
            return;
        if (newWindow.Handle == WindowEx.Handle)
            return;
        if (newWindow.ClassName is
            "Shell_TrayWnd" // Taskbar
            or "Progman" // Desktop
            or "WindowsDashboard" // I forget
            or "Windows.UI.Core.CoreWindow" // Quick Settings and Notification Center (other uwp apps should already be ApplicationFrameHost)
            )
            return;
        // Check if United Sets has owner (United Sets in United Sets)
        if (WindowEx.Root.Children.Any(x => x == newWindow))
            return;
        if (Tabs.Any(x => x.Windows.Any(y => y == newWindow)))
            return;
        var newTab = new HwndHostTab(this, newWindow);
        if (index.HasValue)
            Tabs.Insert(index.Value, newTab);
        else
            Tabs.Add(newTab);
        TabView.SelectedItem = newTab;
    }

    [Event(typeof(SelectionChangedEventHandler))]
    void TabSelectionChanged()
    {
        UnitedSetsHomeBackground.Visibility =
                TabView.SelectedIndex != -1 && Tabs[TabView.SelectedIndex] is CellTab ?
                Visibility.Collapsed :
                Visibility.Visible;

        if (TabView.SelectedIndex is not -1)
        {
            Title = $"{Tabs[TabView.SelectedIndex].Title} (+{Tabs.Count-1} Tabs) - United Sets";
        } else
        {
            Title = "United Sets";
        }
    }
#pragma warning disable CA1822 // Mark members as static
    
    [Event(typeof(TypedEventHandler<TabView, TabViewTabDragStartingEventArgs>))]
    void TabDragStarting(TabViewTabDragStartingEventArgs args)
    {
        if (args.Item is HwndHostTab item)
            args.Data.SetData(UnitedSetsTabWindowDragProperty, (long)item.Window.Handle.Value);
    }


    [Event(typeof(DragEventHandler))]
    void OnDragItemOverTabView(DragEventArgs e)
    {
        if (e.DataView.AvailableFormats.Contains(UnitedSetsTabWindowDragProperty))
            e.AcceptedOperation = DataPackageOperation.Move;
    }
#pragma warning restore CA1822 // Mark members as static

    [Event(typeof(DragEventHandler))]
    void OnDragOverTabViewItem(object sender)
    {
        if (sender is TabViewItem tvi && tvi.Tag is TabBase tb)
            TabView.SelectedIndex = Tabs.IndexOf(tb);
    }

    [Event(typeof(DragEventHandler))]
    async void OnDropOverTabView(DragEventArgs e)
    {
        if (e.DataView.AvailableFormats.Contains(UnitedSetsTabWindowDragProperty))
        {
            var a = (long)await e.DataView.GetDataAsync(UnitedSetsTabWindowDragProperty);
            var window = WindowEx.FromWindowHandle((nint)a);
            var ret = PInvoke.SendMessage(window.Owner, UnitedSetCommunicationChangeWindowOwnership, new(), new(window));
            var pt = e.GetPosition(TabView);
            var finalIdx = (
                from index in Enumerable.Range(0, Tabs.Count)
                let ele = TabView.ContainerFromIndex(index) as UIElement
                let posele = ele.TransformToVisual(TabView).TransformPoint(default)
                let size = ele.ActualSize
                let IsMoreThanTopLeft = pt.X >= posele.X && pt.Y >= posele.Y
                let IsLessThanBotRigh = pt.X <= posele.X + size.X && pt.Y <= posele.Y + size.Y
                where IsMoreThanTopLeft && IsLessThanBotRigh
                select (int?)index
            ).FirstOrDefault();
            AddTab(window, finalIdx);
        }
    }

#pragma warning disable CA1822 // Mark members as static
    [Event(typeof(TypedEventHandler<TabView, TabViewTabDroppedOutsideEventArgs>))]
    void TabDroppedOutside(TabViewTabDroppedOutsideEventArgs args)
    {
        if (args.Tab.Tag is TabBase Tab)
            Tab.DetachAndDispose(JumpToCursor: true);
    }
#pragma warning restore CA1822 // Mark members as static

    // UI Thread
    [Event(typeof(TypedEventHandler<DispatcherQueueTimer, object>))]
    void OnTimerLoopTick()
    {
        var Pt = MainAreaBorder.TransformToVisual(Content).TransformPoint(
            new Point(0, 0)
        );
        var size = MainAreaBorder.ActualSize;
        CacheMiddleAreaBounds = new System.Drawing.Rectangle((int)Pt._x, (int)Pt._y, (int)size.X, (int)size.Y);
        var idx = TabView.SelectedIndex;
        SelectedTabCache = idx < 0 ? null : (idx >= Tabs.Count ? null : Tabs[idx]);
    }

    // Different Thread
    void OnLoopCalled()
    {
        var HasOwner = this.HasOwner;
        if (_HasOwner != HasOwner)
        {
            _HasOwner = HasOwner;
            DispatcherQueue.TryEnqueue(delegate
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.HasOwner)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SettingsButtonVisibility)));
            });
        }
        foreach (var Tab in Tabs)
        {
            if (Tab.IsDisposed)
            {
                Tabs.Remove(Tab);
                break;
            }
        }
        {
            static bool IsInTitleBarBounds(WindowEx Main, WindowEx ToCheck)
            {
                var Bounds = Main.Bounds;
                var CursorPos = Cursor.Position;
                if (Bounds.Contains(CursorPos))
                {
                    var foregroundBounds = ToCheck.Bounds;
                    foregroundBounds.Height -= ToCheck.ClientBounds.Height;
                    if (foregroundBounds.Height is <= 16 ||
                        foregroundBounds.Height >> 2 > foregroundBounds.Height) // A >> 2 == A / 2
                        foregroundBounds.Height = 32 * ToCheck.CurrentDisplay.ScaleFactor / 100;
                    if (foregroundBounds.Contains(CursorPos))
                        return true;
                }
                return false;
            }
            static bool IsUnitedSetWindowVisible(WindowEx WindowEx, WindowEx ToCheck)
            {
                if (ToCheck.Bounds.Contains(WindowEx.Bounds))
                    // User can't see United Sets.
                    // User doesn't know United Sets is behind. We can't mess with them.
                    return false;
                foreach (var below in new WindowRelative(ToCheck).GetBelows())
                {
                    Debug.WriteLine(below);
                    var CursorPos = Cursor.Position;
                    if (below == WindowEx)
                        return true;
                    if (below.ClassName is
                        "Qt5152TrayIconMessageWindowClass" or
                        "Qt5152QWindowIcon")
                        continue;
                    if (!below.IsVisible) continue;
                    if (below.Bounds.Contains(CursorPos))
                        // Also Check Region
                        if (below.Region is not System.Drawing.Rectangle rect ||
                            new System.Drawing.Rectangle(below.Bounds.X + rect.X, below.Bounds.Y + rect.Y,
                            rect.Width, rect.Height).Contains(CursorPos))
                            // If there is window above United Sets and it covers up United Sets
                            // Don't add tabs. User can't see the window
                            return false;
                }
                return false;
            }
            Cell? DetectCell()
            {
                var cursorPos = Cursor.Position;
                var windowBounds = WindowEx.Bounds;
                var diffPos = (X: (double)cursorPos.X - windowBounds.X, Y: (double)cursorPos.Y - windowBounds.Y);
                var scale = WindowEx.CurrentDisplay.ScaleFactor / 100d;
                var area = CacheMiddleAreaBounds.Location;
                diffPos = (diffPos.X - area.X * scale, diffPos.Y - area.Y * scale);
                if (diffPos is { X: > 0, Y: > 0 })
                {
                    if (SelectedTabCache is CellTab CellTab)
                    {
                        var normPos = (diffPos.X / windowBounds.Width, diffPos.Y / windowBounds.Height);
                        var info = GetCellAtCursor(normPos, CellTab.MainCell);
                        if (info is not null)
                        {
                            var (rect, cell) = info.Value;
                            return cell;
                        }
                    }
                }
                return null;
            }
            if (Cursor.IsLeftButtonDown && Keyboard.IsControlDown)
            {
                WindowEx OtherWindowDragging = default;
                Cell? SelectedCell = null;
                do
                {
                    var foregroundWindow = WindowEx.ForegroundWindow;
                    if (foregroundWindow != WindowEx)
                    {
                        if (IsInTitleBarBounds(WindowEx, foregroundWindow) && IsUnitedSetWindowVisible(WindowEx, foregroundWindow))
                        {
                            var NewCell = DetectCell();
                            var UpdateHoverToTrue = OtherWindowDragging == default;
                            if (NewCell != SelectedCell)
                                if (SelectedCell is not null)
                                    SelectedCell.HoverEffect = false;
                            if (NewCell is not null)
                                NewCell.HoverEffect = true;
                            if (NewCell is not null)
                            {
                                if (SelectedCell is null && UpdateHoverToTrue == false)
                                    DispatcherQueue.TryEnqueue(() => NoWindowHoveringStoryBoard.Begin());
                            }
                            else if (UpdateHoverToTrue || (SelectedCell is not null && NewCell is null))
                                DispatcherQueue.TryEnqueue(() => WindowHoveringStoryBoard.Begin());
                            SelectedCell = NewCell;
                            OtherWindowDragging = foregroundWindow;
                        }
                        else
                        {
                            if (SelectedCell is not null)
                                SelectedCell.HoverEffect = false;
                            SelectedCell = null;
                            if (OtherWindowDragging != default)
                                DispatcherQueue.TryEnqueue(() => NoWindowHoveringStoryBoard.Begin());
                            OtherWindowDragging = default;
                        }
                    }
                    Thread.Sleep(200);
                } while (Cursor.IsLeftButtonDown);
                if (OtherWindowDragging != default)
                {
                    var window = OtherWindowDragging;
                    OtherWindowDragging = default;
                    DispatcherQueue.TryEnqueue(() => NoWindowHoveringStoryBoard.Begin());
                    var foreground = WindowEx.ForegroundWindow;
                    if (foreground == window &&
                        IsInTitleBarBounds(WindowEx, window) &&
                        IsUnitedSetWindowVisible(WindowEx, window))
                    {
                        if (SelectedCell is not null)
                        {
                            SelectedCell.HoverEffect = false;
                            DispatcherQueue.TryEnqueue(() => SelectedCell.RegisterWindow(window));
                        }
                        else DispatcherQueue.TryEnqueue(() => AddTab(window));
                    }
                }
            }
        }
    }
    static ((double X1, double Y1, double X2, double Y2), Cell)? GetCellAtCursor((double X, double Y) CursorPos, Cell MainCell)
    {
        if (MainCell.HasWindow)
            return null;
        if (MainCell.Empty)
            return ((0, 0, 1, 1), MainCell);
        static (int Index, double RemainingScaled) ComputeScale(int count, double pos)
        {
            // 1 / count * x = value
            // x = value * count
            var idx = (int)(pos * count);
            if (idx == count) idx--;
            // [ pos - (idx / count) ] * count
            // = pos * count - idx
            var remaining = pos * count - idx;
            return (idx, remaining);
        }
        static (double Out1, double Out2) ComputeScaleReversed((double In1, double In2) scaledRect, int idx, int totalCount)
        {
            return (scaledRect.In1 / totalCount + idx / totalCount, scaledRect.In2 / totalCount + idx / totalCount);
        }
        if (MainCell.HasHorizontalSubCells)
        {
            var count = MainCell.SubCells!.Length;
            var (idx, remaining) = ComputeScale(count, CursorPos.X);
            var output = GetCellAtCursor((remaining, CursorPos.Y), MainCell.SubCells[idx]);
            if (output is null) return null;
            var (Rect, cell) = output.Value;
            (Rect.X1, Rect.X2) = ComputeScaleReversed((Rect.X1, Rect.X2), idx, count);
            return (Rect, cell);
        }
        if (MainCell.HasVerticalSubCells)
        {
            var count = MainCell.SubCells!.Length;
            var (idx, remaining) = ComputeScale(count, CursorPos.Y);
            var output = GetCellAtCursor((CursorPos.X, remaining), MainCell.SubCells[idx]);
            if (output is null) return null;
            var (Rect, cell) = output.Value;
            (Rect.Y1, Rect.Y2) = ComputeScaleReversed((Rect.Y1, Rect.Y2), idx, count);
            return (Rect, cell);
        }
        return null;
    }

    readonly ContentDialog ClosingWindowDialog = new()
    {
        Title = "Closing UnitedSets",
        Content = "How do you want to close the app?",
        PrimaryButtonText = "Release all Windows",
        SecondaryButtonText = "Close all Windows",
        CloseButtonText = "Cancel"
    };

    [Event(typeof(TypedEventHandler<AppWindow, AppWindowClosingEventArgs>))]
    async void OnWindowClosing(AppWindowClosingEventArgs e)
    {
        e.Cancel = true;
        ClosingWindowDialog.XamlRoot = Content.XamlRoot;
        var item = TabView.SelectedItem;
        TabView.SelectedIndex = -1;
        TabView.Visibility = Visibility.Collapsed;
        WindowEx.Focus();
        ContentDialogResult result;
        try
        {
            result = await ClosingWindowDialog.ShowAsync();
        }
        catch
        {
            result = ContentDialogResult.None;
        }
        switch (result)
        {
            case ContentDialogResult.Primary:
                // Release all windows
                while (Tabs.Count > 0)
                {
                    var Tab = Tabs[0];
                    Tabs.RemoveAt(0);
                    Tab.DetachAndDispose(JumpToCursor: false);
                }
                Environment.Exit(0);
                return;
            case ContentDialogResult.Secondary:
                // Close all windows
                TabView.Visibility = Visibility.Visible;
                await Task.Delay(100);
                foreach (var Tab in Tabs.ToArray()) // ToArray = Clone Collection
                {
                    try
                    {
                        _ = Tab.TryCloseAsync();
                        // Try closing tab in 3 second, otherwise give up
                        for (int i = 0; i < 30; i++)
                        {
                            await Task.Delay(100);
                            if (!Tab.IsDisposed) continue;
                        }
                        if (!Tab.IsDisposed) break;
                    }
                    catch
                    {
                        Tab.DetachAndDispose(JumpToCursor: false);
                    }
                }
                if (Tabs.Count == 0)
                {
                    Environment.Exit(0);
                    return;
                }
                goto default;
            default:
                // Do not close window
                try
                {
                    TabView.SelectedItem = item;
                }
                catch
                {
                    if (Tabs.Count > 0)
                        TabView.SelectedIndex = 0;
                }
                TabView.Visibility = Visibility.Visible;
                break;
        }
    }
}