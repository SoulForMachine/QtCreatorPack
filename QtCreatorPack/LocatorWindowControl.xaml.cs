//------------------------------------------------------------------------------
// <copyright file="LocatorWindowControl.xaml.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace QtCreatorPack
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using System.Collections.Generic;
    using System.Windows.Data;

    /// <summary>
    /// Interaction logic for LocatorWindowControl.
    /// </summary>
    public partial class LocatorWindowControl : UserControl
    {
        private Locator _locator;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocatorWindowControl"/> class.
        /// </summary>
        public LocatorWindowControl()
        {
            this.InitializeComponent();
            progressBar.Visibility = Visibility.Hidden;
        }

        internal void SetLocator(Locator locator)
        {
            _locator = locator;
            _locator.SearchResultEvent += _locator_SearchResultEvent;
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
            listView.Items.Clear();
            GridView gridView = listView.View as GridView;
            gridView.Columns.Clear();

            if (_locator != null && textBox.Text.Length > 0)
            {
                progressBar.Visibility = Visibility.Visible;
                _locator.SearchString(textBox.Text);
            }
        }

        private void _locator_SearchResultEvent(object sender, Locator.SearchResultEventArgs args)
        {
            if (args.Type == Locator.SearchResultEventArgs.ResultType.Data)
            {
                foreach (Locator.Item item in args.Items)
                    listView.Items.Add(item);
            }
            else if (args.Type == Locator.SearchResultEventArgs.ResultType.Progress)
            {
                // Update progress bar.
                bool indeterminate = (args.Percent < 0);
                if (progressBar.IsIndeterminate != indeterminate)
                    progressBar.IsIndeterminate = indeterminate;
                progressBar.Value = args.Percent;
            }
            else if (args.Type == Locator.SearchResultEventArgs.ResultType.HeaderData)
            {
                listView.Items.Clear();
                GridView gridView = listView.View as GridView;
                gridView.Columns.Clear();

                foreach (Locator.Item.HeaderData headerData in args.HeaderData)
                {
                    GridViewColumn column = new GridViewColumn();
                    column.Header = headerData.Title;
                    column.DisplayMemberBinding = new Binding(headerData.BoundPropertyName);
                    column.Width = headerData.Width;
                    gridView.Columns.Add(column);
                }
            }
            else if (args.Type == Locator.SearchResultEventArgs.ResultType.Finished)
            {
                progressBar.Visibility = Visibility.Hidden;
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