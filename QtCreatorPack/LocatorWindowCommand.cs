//------------------------------------------------------------------------------
// <copyright file="LocatorWindowCommand.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using System.Globalization;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio;

namespace QtCreatorPack
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class LocatorWindowCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("5e696770-f766-45a4-b37a-32cd249b0f01");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package package;

        private IVsSolution _solution;
        private LocatorWindow _locatorWindow;
        private uint _cookie;
        private Locator _locator;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocatorWindowCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private LocatorWindowCommand(Package package)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            this.package = package;

            OleMenuCommandService commandService = ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var menuCommandID = new CommandID(CommandSet, CommandId);
                var menuItem = new MenuCommand(this.ShowToolWindow, menuCommandID);
                commandService.AddCommand(menuItem);
            }

            // Get the instance number 0 of this tool window. This window is single instance so this instance
            // is actually the only one.
            // The last flag is set to true so that if the tool window does not exists it will be created.
            _locatorWindow = package.FindToolWindow(typeof(LocatorWindow), 0, true) as LocatorWindow;
            if ((null == _locatorWindow) || (null == _locatorWindow.Frame))
            {
                throw new NotSupportedException("Cannot create tool window");
            }

            _locator = new Locator();
            _solution = ServiceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
            _solution.AdviseSolutionEvents(_locator, out _cookie);
            _locatorWindow.SetLocator(_locator);
            _locator.StartWorkerThread();
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static LocatorWindowCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package)
        {
            if (Instance == null)
                Instance = new LocatorWindowCommand(package);
        }

        public static void Deinitialize()
        {
            if (Instance != null)
            {
                Instance.StopLocatorThread();
                Instance = null;
            }
        }

        private void StopLocatorThread()
        {
            _locator.StopWorkerThread();
        }

        /// <summary>
        /// Shows the tool window when the menu item is clicked.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        private void ShowToolWindow(object sender, EventArgs e)
        {
            IVsWindowFrame windowFrame = (IVsWindowFrame)_locatorWindow.Frame;
            ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }
    }
}
