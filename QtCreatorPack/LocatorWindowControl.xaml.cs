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

    /// <summary>
    /// Interaction logic for LocatorWindowControl.
    /// </summary>
    public partial class LocatorWindowControl : UserControl
    {
        private Locator locator;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocatorWindowControl"/> class.
        /// </summary>
        public LocatorWindowControl()
        {
            this.InitializeComponent();
        }

        public void SetSolution(IVsSolution solution)
        {
            if (locator != null)
            {
                locator.StopSearchThread();
            }
            locator = new Locator(solution);
        }

        private void comboBox_TextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            
        }

        private void comboBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {

        }

        private void LocatorToolWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            if (locator != null)
            {
                locator.StopSearchThread();
            }
        }
    }
}