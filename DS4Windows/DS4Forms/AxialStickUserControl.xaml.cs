/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Windows.Controls;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Interaction logic for AxialStickUserControl.xaml
    /// </summary>
    public partial class AxialStickUserControl : UserControl
    {
        private AxialStickControlViewModel axialVM;
        public AxialStickControlViewModel AxialVM
        {
            get => axialVM;
        }

        public AxialStickUserControl()
        {
            InitializeComponent();
        }

        public void UseDevice(StickDeadZoneInfo stickDeadInfo)
        {
            axialVM = new AxialStickControlViewModel(stickDeadInfo);

            // Subscribe to events using existing delegates if possible, or new ones
            // Assuming AxialStickControlViewModel has DeadZoneXChanged/DeadZoneYChanged events as public events
            if (axialVM != null)
            {
                axialVM.DeadZoneXChanged += AxialVM_DeadZoneXChanged;
                axialVM.DeadZoneYChanged += AxialVM_DeadZoneYChanged;
            }

            mainGrid.DataContext = axialVM;
            UpdateVisuals();
        }

        private void AxialVM_DeadZoneXChanged(object sender, EventArgs e)
        {
            UpdateVisuals();
        }

        private void AxialVM_DeadZoneYChanged(object sender, EventArgs e)
        {
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (axialVM == null) return;

            // Canvas is 100x100
            double width = axialVM.DeadZoneX * 100.0;
            double height = axialVM.DeadZoneY * 100.0;

            // Clamp values
            width = Math.Max(0, Math.Min(100, width));
            height = Math.Max(0, Math.Min(100, height));

            // X Band (Vertical) - Width is deadzone X
            visualDeadX.Width = width;
            Canvas.SetLeft(visualDeadX, 50.0 - (width / 2.0));

            // Y Band (Horizontal) - Height is deadzone Y
            visualDeadY.Height = height;
            Canvas.SetTop(visualDeadY, 50.0 - (height / 2.0));

            // Center (Red)
            visualDeadCenter.Width = width;
            visualDeadCenter.Height = height;
            Canvas.SetLeft(visualDeadCenter, 50.0 - (width / 2.0));
            Canvas.SetTop(visualDeadCenter, 50.0 - (height / 2.0));
        }

        public void UnregisterDataContext()
        {
            // Unsubscribe if needed, though VM is usually discarded
            if (axialVM != null)
            {
                axialVM.DeadZoneXChanged -= AxialVM_DeadZoneXChanged;
                axialVM.DeadZoneYChanged -= AxialVM_DeadZoneYChanged;
            }
            mainGrid.DataContext = null;
        }
    }
}
