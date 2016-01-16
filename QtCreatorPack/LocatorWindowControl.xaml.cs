//------------------------------------------------------------------------------
// <copyright file="LocatorWindowControl.xaml.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace QtCreatorPack
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Data;
    using System.Collections.Generic;
    using System.Collections.Specialized;

    /// <summary>
    /// Interaction logic for LocatorWindowControl.
    /// </summary>
    public partial class LocatorWindowControl : UserControl
    {
        private enum ScrollDir
        {
            Down,
            Up
        }

        // A list that implements INotifyCollectionChanged interface so that it can notify the bound ListView control.
        // Item properties do not change so there is no need to implement INotifyPropertyChanged.
        private class ObservableList<T> : List<T>, INotifyCollectionChanged
        {
            public event NotifyCollectionChangedEventHandler CollectionChanged;

            public new void AddRange(IEnumerable<T> range)
            {
                base.AddRange(range);
                CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }

            public new void Clear()
            {
                base.Clear();
                CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        private Locator _locator;
        private GridView _gridView;
        private ObservableList<Locator.Item> _resultList = new ObservableList<Locator.Item>();

        /// <summary>
        /// Initializes a new instance of the <see cref="LocatorWindowControl"/> class.
        /// </summary>
        public LocatorWindowControl()
        {
            this.InitializeComponent();
            progressBar.Visibility = Visibility.Hidden;
            _gridView = listView.View as GridView;
            listView.Visibility = Visibility.Hidden;
            listView.ItemsSource = _resultList;
        }

        internal void SetLocator(Locator locator)
        {
            if (_locator != null)
            {
                _locator.SearchResultEvent -= _locator_SearchResultEvent;
                _locator.SolutionEvent -= _locator_SolutionEvent;
                _locator.CancelSearch(true);
                ResetResultList();
                ResetProgressBar();
            }

            _locator = locator;
            if (_locator != null)
            {
                _locator.SearchResultEvent += _locator_SearchResultEvent;
                _locator.SolutionEvent += _locator_SolutionEvent;
            }
        }

        private bool CurrentItemActivated()
        {
            if (listView.SelectedItem != null)
            {
                ((Locator.Item)listView.SelectedItem).ExecuteAction();
                return true;
            }
            return false;
        }

        private void StartNewSearch()
        {
            _locator.SearchString(textBox.Text);
        }

        private void ResetResultList()
        {
            _resultList.Clear();
            _gridView.Columns.Clear();
            listView.Visibility = Visibility.Hidden;
        }

        private void ResetProgressBar()
        {
            progressBar.IsIndeterminate = false;
            progressBar.Visibility = Visibility.Hidden;
        }

        private void _locator_SearchResultEvent(object sender, Locator.SearchResultEventArgs args)
        {
            switch (args.Type)
            {
                case Locator.SearchResultEventArgs.ResultType.Data:
                    _resultList.AddRange(args.Items);
                    break;

                case Locator.SearchResultEventArgs.ResultType.Progress:
                    // Update progress bar.
                    bool indeterminate = (args.Percent < 0);
                    if (progressBar.IsIndeterminate != indeterminate)
                        progressBar.IsIndeterminate = indeterminate;
                    progressBar.Value = args.Percent;
                    break;

                case Locator.SearchResultEventArgs.ResultType.HeaderData:
                    progressBar.Visibility = Visibility.Visible;
                    ResetResultList();
                    listView.Visibility = Visibility.Visible;
                    bool first = true;

                    foreach (Locator.Item.HeaderData headerData in args.HeaderData)
                    {
                        GridViewColumn column = new GridViewColumn();
                        column.Header = headerData.Title;
                        column.Width = headerData.Width;
                        if (first)
                        {
                            // The first column cell has an icon next to the text block.
                            FrameworkElementFactory stackPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
                            stackPanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

                            FrameworkElementFactory image = new FrameworkElementFactory(typeof(Image));
                            image.SetBinding(Image.SourceProperty, new Binding("Image"));
                            stackPanelFactory.AppendChild(image);

                            FrameworkElementFactory title = new FrameworkElementFactory(typeof(TextBlock));
                            title.SetBinding(TextBlock.TextProperty, new Binding(headerData.BoundPropertyName));
                            title.SetValue(TextBlock.PaddingProperty, new Thickness(20, 0, 0, 0));
                            stackPanelFactory.AppendChild(title);

                            DataTemplate dataTemplate = new DataTemplate();
                            dataTemplate.VisualTree = stackPanelFactory;
                            column.CellTemplate = dataTemplate;
                            first = false;
                        }
                        else
                        {
                            column.DisplayMemberBinding = new Binding(headerData.BoundPropertyName);
                        }
                        _gridView.Columns.Add(column);
                    }
                    break;

                case Locator.SearchResultEventArgs.ResultType.Canceled:
                    ResetResultList();
                    break;

                case Locator.SearchResultEventArgs.ResultType.Finished:
                    ResetProgressBar();
                    break;

                case Locator.SearchResultEventArgs.ResultType.Error:
                    ResetProgressBar();
                    ResetResultList();
                    break;
            }
        }

        private void _locator_SolutionEvent(object sender, Locator.SolutionEventArgs args)
        {
            switch (args.Type)
            {
                case Locator.SolutionEventArgs.EventType.ProjectLoading:
                case Locator.SolutionEventArgs.EventType.ProjectUnloading:
                    progressBar.IsIndeterminate = true;
                    progressBar.Visibility = Visibility.Visible;
                    textStatus.Content = args.Message;
                    break;
                case Locator.SolutionEventArgs.EventType.ProjectFinishedLoading:
                case Locator.SolutionEventArgs.EventType.ProjectFinishedUnloading:
                    ResetProgressBar();
                    textStatus.Content = "";
                    break;
                case Locator.SolutionEventArgs.EventType.SolutionUnloading:
                    textBox.Clear();    // Will trigger TextChanged event and reset everything.
                    break;
            }
        }

        private void textBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            StartNewSearch();
        }

        private void textBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (listView.SelectedIndex == -1)
                    {
                        if (_resultList.Count > 0)
                        {
                            listView.SelectedIndex = 0;
                            listView.ScrollIntoView(listView.SelectedItem);
                        }
                    }
                    else if (listView.SelectedIndex < listView.Items.Count - 1)
                    {
                        ++listView.SelectedIndex;
                        listView.ScrollIntoView(listView.SelectedItem);
                    }
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (listView.SelectedIndex > 0)
                    {
                        --listView.SelectedIndex;
                        listView.ScrollIntoView(listView.SelectedItem);
                    }
                    e.Handled = true;
                    break;

                case Key.PageDown:
                    ScrollListViewPage(ScrollDir.Down);
                    e.Handled = true;
                    break;

                case Key.PageUp:
                    ScrollListViewPage(ScrollDir.Up);
                    e.Handled = true;
                    break;

                case Key.Home:
                    if (Keyboard.IsKeyDown(Key.LeftCtrl) && _resultList.Count > 0)
                    {
                        listView.ScrollIntoView(_resultList[0]);
                        listView.SelectedIndex = 0;
                        e.Handled = true;
                    }
                    break;

                case Key.End:
                    if (Keyboard.IsKeyDown(Key.LeftCtrl) && _resultList.Count > 0)
                    {
                        listView.ScrollIntoView(_resultList[_resultList.Count - 1]);
                        listView.SelectedIndex = _resultList.Count - 1;
                        e.Handled = true;
                    }
                    break;

                case Key.Enter:
                    if (CurrentItemActivated())
                        e.Handled = true;
                    break;
            }
        }

        private void ScrollListViewPage(ScrollDir dir)
        {
            if (_resultList.Count == 0)
                return;

            VirtualizingStackPanel vsp = Utils.FindVisualChild<VirtualizingStackPanel>(listView);
            if (vsp != null)
            {
                int firstVisibleItem = (int)vsp.VerticalOffset;
                int visibleItemCount = (int)vsp.ViewportHeight;
                int selectedItem = listView.SelectedIndex;
                int firstItem = (selectedItem >= 0) ? selectedItem : firstVisibleItem;

                int scrollItem;
                if (dir == ScrollDir.Down)
                    scrollItem = System.Math.Min(firstItem + visibleItemCount - 1, _resultList.Count - 1);
                else
                    scrollItem = System.Math.Max(firstItem - visibleItemCount - 1, 0);

                listView.ScrollIntoView(_resultList[scrollItem]);
                listView.SelectedIndex = scrollItem;
            }
        }

        private void textBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.IsRepeat)
            {
                StartNewSearch();
            }
        }

        private void listView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.IsRepeat)
                CurrentItemActivated();
        }

        private void listView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CurrentItemActivated();
        }

        private void buttonClear_Click(object sender, RoutedEventArgs e)
        {
            textBox.Clear();
            textBox.Focus();
        }

        private void textBox_GotFocus(object sender, RoutedEventArgs e)
        {
            textBox.SelectAll();
        }
    }
}