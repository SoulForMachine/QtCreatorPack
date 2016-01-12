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

    /// <summary>
    /// Interaction logic for LocatorWindowControl.
    /// </summary>
    public partial class LocatorWindowControl : UserControl
    {
        private enum LocatorState
        {
            Uninitialized,
            Ready,
            Searching
        }

        private Locator _locator;
        private LocatorState _locatorState;
        private GridView _gridView;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocatorWindowControl"/> class.
        /// </summary>
        public LocatorWindowControl()
        {
            this.InitializeComponent();
            progressBar.Visibility = Visibility.Hidden;
            _locatorState = LocatorState.Uninitialized;
            _gridView = listView.View as GridView;
        }

        internal void SetLocator(Locator locator)
        {
            if (_locator != null)
            {
                _locator.SearchResultEvent -= _locator_SearchResultEvent;
                _locator.ProjectProcessingEvent -= _locator_ProjectProcessingEvent;
                if (_locatorState == LocatorState.Searching)
                {
                    _locator.CancelSearch(true);
                    ResetResultList();
                    ResetProgressBar();
                }
            }

            _locator = locator;
            if (_locator != null)
            {
                _locatorState = LocatorState.Ready;
                _locator.SearchResultEvent += _locator_SearchResultEvent;
                _locator.ProjectProcessingEvent += _locator_ProjectProcessingEvent;
            }
            else
                _locatorState = LocatorState.Uninitialized;
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
            if (_locator != null)
            {
                if (textBox.Text.Length > 0)
                {
                    _locatorState = LocatorState.Searching;
                    _locator.SearchString(textBox.Text);
                }
                else
                {
                    ResetResultList();
                    ResetProgressBar();
                    _locator.CancelSearch();
                }
            }
        }

        private void ResetResultList()
        {
            listView.Items.Clear();
            _gridView.Columns.Clear();
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
                    foreach (Locator.Item item in args.Items)
                        listView.Items.Add(item);
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
                    bool first = true;

                    foreach (Locator.Item.HeaderData headerData in args.HeaderData)
                    {
                        GridViewColumn column = new GridViewColumn();
                        column.Header = headerData.Title;
                        column.Width = headerData.Width;
                        if (first)
                        {
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
                    _locatorState = LocatorState.Ready;
                    break;

                case Locator.SearchResultEventArgs.ResultType.Finished:
                case Locator.SearchResultEventArgs.ResultType.Error:
                    ResetProgressBar();
                    _locatorState = LocatorState.Ready;
                    break;
            }
        }

        private void _locator_ProjectProcessingEvent(object sender, Locator.ProjectProcessingEventArgs args)
        {
            switch (args.Type)
            {
                case Locator.ProjectProcessingEventArgs.ProcessingType.Loading:
                case Locator.ProjectProcessingEventArgs.ProcessingType.Unloading:
                    progressBar.IsIndeterminate = true;
                    progressBar.Visibility = Visibility.Visible;
                    textStatus.Content = args.Message;
                    break;
                case Locator.ProjectProcessingEventArgs.ProcessingType.Finished:
                    ResetProgressBar();
                    textStatus.Content = "";
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
                        listView.SelectedIndex = 0;
                    }
                    else if (listView.SelectedIndex < listView.Items.Count - 1)
                    {
                        ++listView.SelectedIndex;
                    }
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (listView.SelectedIndex > 0)
                    {
                        --listView.SelectedIndex;
                    }
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (CurrentItemActivated())
                        e.Handled = true;
                    break;
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
    }
}