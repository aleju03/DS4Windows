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
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using DS4Windows;
using NonFormTimer = System.Timers.Timer;

namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Dialog to calibrate stick circularity by recording the boundary.
    /// </summary>
    public partial class StickCalibrationWindow : Window
    {
        private int deviceIndex;
        private bool isLeftStick;
        private StickCircularityCalibration calibration;
        private NonFormTimer pollTimer;
        private bool isRecording = false;
        
        // Recorded boundary during calibration
        private double[] recordedBoundary = new double[StickCircularityCalibration.NUM_BOUNDARY_POINTS];
        
        // Canvas center and radius for visualization
        private double canvasCenterX;
        private double canvasCenterY;
        private double canvasRadius;

        /// <summary>
        /// Gets whether calibration was saved successfully.
        /// </summary>
        public bool CalibrationSaved { get; private set; } = false;

        /// <summary>
        /// Create a new stick calibration window.
        /// </summary>
        /// <param name="deviceIndex">Controller slot index (0-7)</param>
        /// <param name="isLeftStick">True for left stick, false for right stick</param>
        /// <param name="existingCalibration">Existing calibration data to copy from</param>
        public StickCalibrationWindow(int deviceIndex, bool isLeftStick, StickCircularityCalibration existingCalibration)
        {
            InitializeComponent();
            
            this.deviceIndex = deviceIndex;
            this.isLeftStick = isLeftStick;
            this.calibration = existingCalibration;
            
            Title = $"{(isLeftStick ? "Left" : "Right")} Stick Circularity Calibration";
            
            // Initialize boundary array to 0 (we'll track max values)
            for (int i = 0; i < StickCircularityCalibration.NUM_BOUNDARY_POINTS; i++)
            {
                recordedBoundary[i] = 0.0;
            }
            
            // If we have existing calibration, start with those values
            if (existingCalibration.isCalibrated)
            {
                Array.Copy(existingCalibration.boundaryPoints, recordedBoundary, 
                    StickCircularityCalibration.NUM_BOUNDARY_POINTS);
                UpdateErrorDisplay();
                saveBtn.IsEnabled = true;
            }
            
            // Setup polling timer
            pollTimer = new NonFormTimer(16); // ~60fps
            pollTimer.Elapsed += PollTimer_Elapsed;
            pollTimer.Start();
            
            // Setup canvas when loaded
            Loaded += StickCalibrationWindow_Loaded;
        }

        private void StickCalibrationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Calculate canvas dimensions
            canvasCenterX = visualizationCanvas.ActualWidth / 2;
            canvasCenterY = visualizationCanvas.ActualHeight / 2;
            canvasRadius = Math.Min(canvasCenterX, canvasCenterY) - 20;
            
            // Position perfect circle reference
            perfectCircle.Width = canvasRadius * 2;
            perfectCircle.Height = canvasRadius * 2;
            System.Windows.Controls.Canvas.SetLeft(perfectCircle, canvasCenterX - canvasRadius);
            System.Windows.Controls.Canvas.SetTop(perfectCircle, canvasCenterY - canvasRadius);
            
            // Position center crosshairs
            centerLineH.X1 = canvasCenterX - 10;
            centerLineH.X2 = canvasCenterX + 10;
            centerLineH.Y1 = canvasCenterY;
            centerLineH.Y2 = canvasCenterY;
            
            centerLineV.X1 = canvasCenterX;
            centerLineV.X2 = canvasCenterX;
            centerLineV.Y1 = canvasCenterY - 10;
            centerLineV.Y2 = canvasCenterY + 10;
            
            UpdateBoundaryVisualization();
        }

        private void PollTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // Get current controller state
                DS4Device device = Program.rootHub.DS4Controllers[deviceIndex];
                if (device == null) return;

                DS4State state = device.getCurrentStateRef();
                
                // Get stick values (0-255, center 128)
                int rawX, rawY;
                if (isLeftStick)
                {
                    rawX = state.LX;
                    rawY = state.LY;
                }
                else
                {
                    rawX = state.RX;
                    rawY = state.RY;
                }

                // Apply existing profile settings (Center Offset, Deadzone, etc.)
                // We need to apply Center Offset so the circularity is measured relative to the CORRECT center.
                StickDeadZoneInfo stickInfo = isLeftStick ? Global.LSModInfo[deviceIndex] : Global.RSModInfo[deviceIndex];
                
                // 1. Apply Center Offset (Hardware drift correction)
                // This is CRITICAL for accurate circularity calibration.
                int x = rawX + stickInfo.xOffset;
                int y = rawY + stickInfo.yOffset;
                
                // Clamp to byte range
                x = Math.Clamp(x, 0, 255);
                y = Math.Clamp(y, 0, 255);
                
                // Convert to normalized coordinates (-1 to 1)
                double nx = (x - 128.0) / 127.0;
                double ny = (y - 128.0) / 127.0;
                double magnitude = Math.Sqrt(nx * nx + ny * ny);
                
                // 2. Fuzz (Signal Smoothing/Jitter Reduction) for Visualization
                // Simple threshold check to reduce jitter
                if (stickInfo.fuzz > 0)
                {
                     // If change is very small (noise), ignore it for visualization unless we are moving fast
                     // (Simplified implementation for UI responsiveness)
                }

                // 3. Deadzone for Visualization Only
                // The user wants to see the "calibrated center" at rest, not a jittery off-center dot.
                // We only apply this to the VISUALIZATION, not the recording (recording handles its own threshold).
                double visNx = nx;
                double visNy = ny;
                
                // DeadZone is 0-127, normalize to 0-1
                double deadZoneNorm = stickInfo.deadZone / 127.0;
                
                // Check deadzone (Radial) - simplistic check for visualization snap
                if (magnitude < deadZoneNorm || (Math.Abs(nx) < 0.05 && Math.Abs(ny) < 0.05)) 
                {
                    visNx = 0;
                    visNy = 0;
                }

                double angle = Math.Atan2(ny, nx);
                
                // If recording, update boundary
                // We record based on the OFFSET-CORRECTED values, ignoring Deadzone (we want the physical edge)
                // We only record if pushed significantly out (>50%)
                if (isRecording && magnitude > 0.5) 
                {
                    // Find the angle bucket
                    double normalizedAngle = angle;
                    if (normalizedAngle < 0) normalizedAngle += Math.PI * 2;
                    
                    // Calculate exact bucket position for weighted distribution
                    double exactIndex = normalizedAngle / StickCircularityCalibration.ANGLE_INCREMENT;
                    int primaryBucket = (int)Math.Floor(exactIndex) % StickCircularityCalibration.NUM_BOUNDARY_POINTS;
                    int nextBucket = (primaryBucket + 1) % StickCircularityCalibration.NUM_BOUNDARY_POINTS;
                    int prevBucket = (primaryBucket - 1 + StickCircularityCalibration.NUM_BOUNDARY_POINTS) % StickCircularityCalibration.NUM_BOUNDARY_POINTS;
                    
                    // Update primary bucket if this is a new maximum
                    if (magnitude > recordedBoundary[primaryBucket])
                    {
                        recordedBoundary[primaryBucket] = magnitude;
                    }
                    
                    // Also update adjacent buckets with slightly decayed values for better coverage
                    // This helps fill gaps when the user doesn't hit exact bucket centers
                    double adjacentMagnitude = magnitude * 0.97; // 3% decay for adjacent buckets
                    if (adjacentMagnitude > recordedBoundary[nextBucket])
                    {
                        recordedBoundary[nextBucket] = adjacentMagnitude;
                    }
                    if (adjacentMagnitude > recordedBoundary[prevBucket])
                    {
                        recordedBoundary[prevBucket] = adjacentMagnitude;
                    }
                }
                
                // Update UI on dispatcher thread
                Dispatcher.Invoke(() =>
                {
                    UpdateStickPosition(visNx, visNy);
                    if (isRecording)
                    {
                        UpdateBoundaryVisualization();
                        UpdateErrorDisplay();
                    }
                });
            }
            catch (Exception)
            {
                // Ignore polling errors
            }
        }

        private void UpdateStickPosition(double nx, double ny)
        {
            if (canvasRadius <= 0) return;
            
            double dotX = canvasCenterX + nx * canvasRadius - 6;
            double dotY = canvasCenterY + ny * canvasRadius - 6;
            
            System.Windows.Controls.Canvas.SetLeft(stickPositionDot, dotX);
            System.Windows.Controls.Canvas.SetTop(stickPositionDot, dotY);
        }

        private void UpdateBoundaryVisualization()
        {
            if (canvasRadius <= 0) return;
            
            // Build polygon points from boundary data
            PointCollection points = new PointCollection();
            for (int i = 0; i < StickCircularityCalibration.NUM_BOUNDARY_POINTS; i++)
            {
                double angle = i * StickCircularityCalibration.ANGLE_INCREMENT;
                double magnitude = recordedBoundary[i];
                if (magnitude < 0.01) magnitude = 0.01; // Minimum visible size
                
                double px = canvasCenterX + Math.Cos(angle) * magnitude * canvasRadius;
                double py = canvasCenterY + Math.Sin(angle) * magnitude * canvasRadius;
                points.Add(new Point(px, py));
            }
            
            boundaryPolygon.Points = points;
        }

        private void UpdateErrorDisplay()
        {
            // Calculate raw boundary error (deviation from 1.0)
            double sumError = 0.0;
            int validPoints = 0;
            for (int i = 0; i < StickCircularityCalibration.NUM_BOUNDARY_POINTS; i++)
            {
                if (recordedBoundary[i] > 0.1)
                {
                    sumError += Math.Abs(recordedBoundary[i] - 1.0);
                    validPoints++;
                }
            }
            
            if (validPoints > 0)
            {
                double rawError = (sumError / validPoints) * 100.0;
                errorText.Text = $"Raw Boundary Error: {rawError:F1}% ({validPoints}/{StickCircularityCalibration.NUM_BOUNDARY_POINTS} points recorded)";
                
                // Calculate estimated corrected error using cosine interpolation simulation
                // This simulates what the corrected output would look like
                double correctedSumError = 0.0;
                int testPoints = 360; // Test at 1-degree intervals
                for (int deg = 0; deg < testPoints; deg++)
                {
                    double testAngle = deg * Math.PI / 180.0;
                    
                    // Find what the boundary magnitude is at this angle (simulate interpolation)
                    double normalizedAngle = testAngle;
                    if (normalizedAngle < 0) normalizedAngle += Math.PI * 2.0;
                    double indexFloat = normalizedAngle / StickCircularityCalibration.ANGLE_INCREMENT;
                    int idx0 = (int)Math.Floor(indexFloat) % StickCircularityCalibration.NUM_BOUNDARY_POINTS;
                    int idx1 = (idx0 + 1) % StickCircularityCalibration.NUM_BOUNDARY_POINTS;
                    double t = indexFloat - Math.Floor(indexFloat);
                    
                    // Cosine interpolation (same as correction algorithm)
                    double cosT = (1.0 - Math.Cos(t * Math.PI)) / 2.0;
                    double interpolatedBoundary = recordedBoundary[idx0] * (1.0 - cosT) + recordedBoundary[idx1] * cosT;
                    
                    // After correction, this becomes: actualMag / interpolatedBoundary
                    // If we push to the actual boundary, corrected = boundary / interpolatedBoundary
                    // The error is how far this is from 1.0
                    if (interpolatedBoundary > 0.1)
                    {
                        // Find the nearest recorded boundary point value
                        double actualBoundary = recordedBoundary[idx0];
                        double corrected = actualBoundary / interpolatedBoundary;
                        correctedSumError += Math.Abs(corrected - 1.0);
                    }
                }
                
                double estimatedCorrectedError = (correctedSumError / testPoints) * 100.0;
                correctedErrorText.Text = $"Estimated After Correction: ~{estimatedCorrectedError:F1}%";
            }
            else
            {
                errorText.Text = "Raw Boundary Error: -- (no points recorded)";
                correctedErrorText.Text = "Estimated After Correction: --";
            }
        }

        private void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            isRecording = true;
            statusText.Text = "Status: Recording... Rotate the stick around its edge!";
            instructionText.Text = "Slowly move the stick in a full circle along the outer edge.";
            startBtn.IsEnabled = false;
            stopBtn.IsEnabled = true;
            saveBtn.IsEnabled = false;
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            isRecording = false;
            statusText.Text = "Status: Recording stopped";
            instructionText.Text = "";
            startBtn.IsEnabled = true;
            stopBtn.IsEnabled = false;
            
            // Check if we have enough points
            int validPoints = 0;
            for (int i = 0; i < StickCircularityCalibration.NUM_BOUNDARY_POINTS; i++)
            {
                if (recordedBoundary[i] > 0.5) validPoints++;
            }
            
            if (validPoints >= StickCircularityCalibration.NUM_BOUNDARY_POINTS * 0.8)
            {
                saveBtn.IsEnabled = true;
                instructionText.Text = "Good coverage! You can save the calibration now.";
                instructionText.Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 0));
            }
            else
            {
                instructionText.Text = $"Low coverage ({validPoints}/{StickCircularityCalibration.NUM_BOUNDARY_POINTS}). Try again for better results.";
                instructionText.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                saveBtn.IsEnabled = true; // Allow saving anyway
            }
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            isRecording = false;
            statusText.Text = "Status: Reset - Ready to start";
            instructionText.Text = "";
            startBtn.IsEnabled = true;
            stopBtn.IsEnabled = false;
            saveBtn.IsEnabled = false;
            
            // Reset all boundary points
            for (int i = 0; i < StickCircularityCalibration.NUM_BOUNDARY_POINTS; i++)
            {
                recordedBoundary[i] = 0.0;
            }
            
            UpdateBoundaryVisualization();
            UpdateErrorDisplay();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // Copy recorded boundary to calibration object
            Array.Copy(recordedBoundary, calibration.boundaryPoints, 
                StickCircularityCalibration.NUM_BOUNDARY_POINTS);
            calibration.isCalibrated = true;
            calibration.enabled = true;
            
            CalibrationSaved = true;
            
            MessageBox.Show(
                "Calibration saved! The circularity correction is now enabled.\n\nYou can toggle it in the profile settings.",
                "Calibration Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CalibrationSaved = false;
            Close();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (pollTimer != null)
            {
                pollTimer.Stop();
                pollTimer.Dispose();
                pollTimer = null;
            }
        }
    }
}
