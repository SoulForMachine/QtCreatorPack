//------------------------------------------------------------------------------
// <copyright file="LocatorWindow.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace QtCreatorPack
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;

    /// <summary>
    /// This class implements the tool window exposed by this package and hosts a user control.
    /// </summary>
    /// <remarks>
    /// In Visual Studio tool windows are composed of a frame (implemented by the shell) and a pane,
    /// usually implemented by the package implementer.
    /// <para>
    /// This class derives from the ToolWindowPane class provided from the MPF in order to use its
    /// implementation of the IVsUIElementPane interface.
    /// </para>
    /// </remarks>
    [Guid("e3b80558-3ff2-45b9-964b-aea9083a8269")]
    public class LocatorWindow : ToolWindowPane
    {
        private LocatorWindowControl locatorWindowControl;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocatorWindow"/> class.
        /// </summary>
        public LocatorWindow() : base(null)
        {
            this.Caption = "LocatorWindow";

            // This is the user control hosted by the tool window; Note that, even if this class implements IDisposable,
            // we are not calling Dispose on this object. This is because ToolWindowPane calls Dispose on
            // the object returned by the Content property.
            locatorWindowControl = new LocatorWindowControl();
            Content = locatorWindowControl;
        }

        public void SetSolution(IVsSolution solution)
        {
            locatorWindowControl.SetSolution(solution);
        }
    }
}
