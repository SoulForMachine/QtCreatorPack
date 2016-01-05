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
    using System.Windows.Data;    /// <summary>
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
        }

        internal void SetLocator(Locator locator)
        {
            _locator = locator;
            _locator.SearchFinishedEvent += _locator_SearchFinishedEvent;
        }

        private void CurrentItemActivated()
        {
            if (listView.SelectedItem != null)
                ((Locator.Item)listView.SelectedItem).ExecuteAction();
        }

        private void _locator_SearchFinishedEvent(object sender, Locator.SearchFinishedEventArgs args)
        {
            bool headerAdded = false;
            foreach (Locator.Item item in args.Items)
            {
                if (!headerAdded)
                {
                    GridView gridView = listView.View as GridView;

                    List<Locator.Item.HeaderData> headerDataList = item.GetHeaderData();
                    foreach (Locator.Item.HeaderData headerData in headerDataList)
                    {
                        GridViewColumn column = new GridViewColumn();
                        column.Header = headerData.Title;
                        column.DisplayMemberBinding = new Binding(headerData.BoundPropertyName);
                        gridView.Columns.Add(column);
                    }
                    headerAdded = true;
                }

                listView.Items.Add(item);
            }
        }

        private void textBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            listView.Items.Clear();
            GridView gridView = listView.View as GridView;
            gridView.Columns.Clear();

            if (_locator != null && textBox.Text.Length > 0)
                _locator.SearchString(textBox.Text);
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
    }
}