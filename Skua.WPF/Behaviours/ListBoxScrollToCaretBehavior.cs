using Microsoft.Xaml.Behaviors;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Skua.WPF;

public class ListBoxScrollToCaretBehavior : Behavior<ListBox>
{
    private ScrollViewer? _scrollViewer;
    private bool _isScrollDownEnabled;
    private INotifyCollectionChanged? _collectionSource;
    private DependencyPropertyDescriptor? _itemsSourceDp;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.Unloaded += OnUnloaded;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.Unloaded -= OnUnloaded;
        
        if (_itemsSourceDp != null)
        {
            _itemsSourceDp.RemoveValueChanged(AssociatedObject, OnItemsSourceChanged);
            _itemsSourceDp = null;
        }

        if (_collectionSource != null)
        {
            CollectionChangedEventManager.RemoveHandler(_collectionSource, OnCollectionChanged);
            _collectionSource = null;
        }

        _scrollViewer = null;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _itemsSourceDp = DependencyPropertyDescriptor.FromProperty(
            ItemsControl.ItemsSourceProperty,
            typeof(ItemsControl));

        if (_itemsSourceDp != null)
            _itemsSourceDp.AddValueChanged(AssociatedObject, OnItemsSourceChanged);

        HookItemsSource();

        if (VisualTreeHelper.GetChildrenCount(AssociatedObject) > 0)
        {
            Border border = (Border)VisualTreeHelper.GetChild(AssociatedObject, 0);
            _scrollViewer = (ScrollViewer)VisualTreeHelper.GetChild(border, 0);
        }
    }

    private void OnItemsSourceChanged(object sender, EventArgs e)
    {
        HookItemsSource();
    }

    private void HookItemsSource()
    {
        var newSrc = AssociatedObject.ItemsSource as INotifyCollectionChanged;
        if (!ReferenceEquals(_collectionSource, newSrc))
        {
            if (_collectionSource != null)
                CollectionChangedEventManager.RemoveHandler(_collectionSource, OnCollectionChanged);

            _collectionSource = newSrc;

            if (_collectionSource != null)
                CollectionChangedEventManager.AddHandler(_collectionSource, OnCollectionChanged);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_itemsSourceDp != null)
        {
            _itemsSourceDp.RemoveValueChanged(AssociatedObject, OnItemsSourceChanged);
            _itemsSourceDp = null;
        }

        if (_collectionSource != null)
        {
            CollectionChangedEventManager.RemoveHandler(_collectionSource, OnCollectionChanged);
            _collectionSource = null;
        }

        _scrollViewer = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollViewer is null)
            return;

        _isScrollDownEnabled = _scrollViewer.ScrollableHeight > 0 && _scrollViewer.VerticalOffset + _scrollViewer.ViewportHeight < _scrollViewer.ExtentHeight;
        if (e.Action == NotifyCollectionChangedAction.Add && !_isScrollDownEnabled)
            _scrollViewer.ScrollToBottom();
    }
}

//using System.Collections.Specialized;
//using System.Windows;
//using System.Windows.Automation.Peers;
//using System.Windows.Automation.Provider;
//using System.Windows.Controls;
//using Microsoft.Xaml.Behaviors;

//namespace Skua.WPF;
//public class ListBoxScrollToCaretBehavior : Behavior<ListBox>
//{
//    private IScrollProvider? _scrollInterface;

//    protected override void OnAttached()
//    {
//        base.OnAttached();
//        AssociatedObject.Loaded += OnLoaded;
//        AssociatedObject.Unloaded += OnUnLoaded;
//    }

//    protected override void OnDetaching()
//    {
//        AssociatedObject.Loaded -= OnLoaded;
//        AssociatedObject.Unloaded -= OnUnLoaded;
//        base.OnDetaching();
//    }

//    private void OnLoaded(object sender, RoutedEventArgs e)
//    {
//        if (AssociatedObject.ItemsSource is not INotifyCollectionChanged incc)
//            return;
//        incc.CollectionChanged += OnCollectionChanged;

//        ListBoxAutomationPeer svAutomation = (ListBoxAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(AssociatedObject);
//        _scrollInterface = (IScrollProvider)svAutomation.GetPattern(PatternInterface.Scroll);
//    }

//    private void OnUnLoaded(object? sender, RoutedEventArgs e)
//    {
//        if (AssociatedObject.ItemsSource is not INotifyCollectionChanged incc)
//            return;

//        incc.CollectionChanged -= OnCollectionChanged;
//    }

//    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
//    {
//        if (_scrollInterface is null)
//            return;

//        System.Windows.Automation.ScrollAmount scrollVertical = System.Windows.Automation.ScrollAmount.LargeIncrement;
//        System.Windows.Automation.ScrollAmount scrollHorizontal = System.Windows.Automation.ScrollAmount.NoAmount;
//        //If the vertical scroller is not available, the operation cannot be performed, which will raise an exception.
//        if (_scrollInterface.VerticallyScrollable)
//            _scrollInterface.Scroll(scrollHorizontal, scrollVertical);
//    }
//}