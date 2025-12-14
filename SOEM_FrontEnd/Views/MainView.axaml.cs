using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SOEM_FrontEnd.ViewModels;

namespace SOEM_FrontEnd.Views;

public partial class MainView : UserControl
{
    private bool _logHooked;
    
    public MainView()
    {
        InitializeComponent();

        //HookLogAutoScroll();
        this.DataContextChanged += OnDataContextChanged;
    }
    
    //로그창 밑으로 내리기 위해 사용되는 코드 비하인드.
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        //
        if (_logHooked)
            return;

        if (DataContext is MainViewModel vm && LogListBox != null)
        {
            vm.LogLines.CollectionChanged += OnLogLinesChanged;
            _logHooked = true;
        }
    }
    
    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems != null &&
            e.NewItems.Count > 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (LogListBox.ItemCount > 0)
                {
                    var last = LogListBox.Items![LogListBox.ItemCount - 1];
                    LogListBox.ScrollIntoView(last);
                }
            });
        }
    }
    

    private void HookLogAutoScroll()
    {
        if (_logHooked)
            return;
        if (DataContext is MainViewModel vm && LogListBox != null)
        {
            vm.LogLines.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (LogListBox.ItemCount > 0)
                        {
                            var lastItem = LogListBox.Items![LogListBox.ItemCount - 1];
                            LogListBox.ScrollIntoView(lastItem);
                        }
                    });
                }
            };
            
            _logHooked = true;
        }
        
    }
}
