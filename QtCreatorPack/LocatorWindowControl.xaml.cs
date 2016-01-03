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
        }

        internal void SetLocator(Locator locator)
        {
            _locator = locator;
            _locator.FileSearchFinishedEvent += _locator_FileSearchFinishedEvent;
            _locator.FunctionSearchFinishedEvent += _locator_FunctionSearchFinishedEvent;
        }

        private void CurrentItemActivated()
        {
            if (listView.SelectedItem == null)
                return;

            if (listView.SelectedItem.GetType() == typeof(Locator.ProjectItem))
            {
                Locator.ProjectItem projectItem = listView.SelectedItem as Locator.ProjectItem;
                projectItem.Item.Open(EnvDTE.Constants.vsViewKindPrimary).Activate();
            }
            else if (listView.SelectedItem.GetType() == typeof(Locator.FunctionItem))
            {
                Locator.FunctionItem funcItem = listView.SelectedItem as Locator.FunctionItem;
            }
        }

        private void _locator_FunctionSearchFinishedEvent(object sender, Locator.FunctionSearchFinishedEventArgs items)
        {
            throw new NotImplementedException();
        }

        private void _locator_FileSearchFinishedEvent(object sender, Locator.FileSearchFinishedEventArgs items)
        {
            foreach (Locator.ProjectItem prjItem in items.Items)
            {
                listView.Items.Add(prjItem);
            }
        }

        private void textBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            listView.Items.Clear();
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