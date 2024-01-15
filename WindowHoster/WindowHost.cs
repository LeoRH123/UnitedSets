﻿using Get.EasyCSharp;
using Get.XAMLTools;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.ComponentModel;
using Windows.Foundation;
using Window = WinWrapper.Windowing.Window;
namespace WindowHoster;
[DependencyProperty<RegisteredWindow>("AssociatedWindow", UseNullableReferenceType = true, GenerateLocalOnPropertyChangedMethod = true)]
public partial class WindowHost : FrameworkElement
{
    Window ParentWindow =>
        Window.FromWindowHandle(
            (nint)XamlRoot.ContentIslandEnvironment.AppWindowId.Value
        );
    public WindowHost()
    {
        Loaded += WindowHost_Loaded;
        Unloaded += WindowHost_Unloaded;
        EffectiveViewportChanged += WindowHost_EffectiveViewportChanged;
    }
    Rect cachedContainerRectangle;
    private void WindowHost_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        if (XamlRoot?.Content is not FrameworkElement rootElement) return;
        var margin = rootElement.Margin;
        var pos = TransformToVisual(rootElement).TransformPoint(
            new(margin.Left, margin.Top)
        );
        var size = ActualSize;
        cachedContainerRectangle = new()
        {
            X = pos.X,
            Y = pos.Y,
            Width = size.X,
            Height = size.Y
        };
        if (Controller is not { } controller) return;
        controller.ContainerRectangle = cachedContainerRectangle;
    }

    partial void OnAssociatedWindowChanged(RegisteredWindow? oldValue, RegisteredWindow? newValue)
    {
        Controller = null;
        if (IsLoaded)
        {
            Controller = newValue?.GetController(ParentWindow, DispatcherQueue);
        }
    }
    RegisteredWindowController? Controller
    {
        get
        {
            return _Controller;
        }

        set
        {
            if (_Controller == value) return;
            BeforeControllerChanged();
            _Controller = value;
            AfterControllerChanged();
        }
    }
    RegisteredWindowController? _Controller;
    void BeforeControllerChanged()
    {
        Controller?.Unregister();
    }
    void AfterControllerChanged()
    {
        if (Controller is not { } controller) return;
        controller.ContainerRectangle = cachedContainerRectangle;
    }
    private void WindowHost_Unloaded(object sender, RoutedEventArgs e)
    {
        Controller = null;
    }

    private void WindowHost_Loaded(object sender, RoutedEventArgs e)
    {
        Controller = null;
        Controller = AssociatedWindow?.GetController(ParentWindow, DispatcherQueue);
    }

}
