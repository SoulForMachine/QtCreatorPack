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
        }

        private void comboBox_TextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            
        }

        private void comboBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // item.Open(Constants.vsViewKindPrimary).Activate();
        }
    }
}