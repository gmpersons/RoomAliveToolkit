﻿using System;
using System.Collections.Generic;
using Microsoft.Kinect;
using RoomAliveToolkit;

namespace Kinect2
{
    public class Kinect2Calibration
    {
        public const int depthImageWidth = 512;
        public const int depthImageHeight = 424;
        public const int colorImageWidth = 1920;
        public const int colorImageHeight = 1080;

        public Matrix colorCameraMatrix = new RoomAliveToolkit.Matrix(3, 3);
        public Matrix colorLensDistortion = new RoomAliveToolkit.Matrix(2, 1);
        public Matrix depthCameraMatrix = new RoomAliveToolkit.Matrix(3, 3);
        public Matrix depthLensDistortion = new RoomAliveToolkit.Matrix(2, 1);
        public Matrix depthToColorTransform = new RoomAliveToolkit.Matrix(4, 4);

        public void RecoverCalibrationFromSensor(KinectSensor kinectSensor)
        {
            var stopWatch = new System.Diagnostics.Stopwatch();
            stopWatch.Start();

            var objectPoints1 = new List<RoomAliveToolkit.Matrix>();
            var colorPoints1 = new List<System.Drawing.PointF>();
            var depthPoints1 = new List<System.Drawing.PointF>();

            int n = 0;
            for (float x = -2f; x < 2f; x += 0.2f)
                for (float y = -2f; y < 2f; y += 0.2f)
                    for (float z = 0.4f; z < 4.5f; z += 0.4f)
                    {
                        var kinectCameraPoint = new CameraSpacePoint();
                        kinectCameraPoint.X = x;
                        kinectCameraPoint.Y = y;
                        kinectCameraPoint.Z = z;

                        // use SDK's projection
                        // adjust Y to make RH cooridnate system that is a projection of Kinect 3D points
                        var kinectColorPoint = kinectSensor.CoordinateMapper.MapCameraPointToColorSpace(kinectCameraPoint);
                        kinectColorPoint.Y = colorImageHeight - kinectColorPoint.Y;
                        var kinectDepthPoint = kinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(kinectCameraPoint);
                        kinectDepthPoint.Y = depthImageHeight - kinectDepthPoint.Y;

                        if ((kinectColorPoint.X >= 0) && (kinectColorPoint.X < colorImageWidth) &&
                            (kinectColorPoint.Y >= 0) && (kinectColorPoint.Y < colorImageHeight) &&
                            (kinectDepthPoint.X >= 0) && (kinectDepthPoint.X < depthImageWidth) &&
                            (kinectDepthPoint.Y >= 0) && (kinectDepthPoint.Y < depthImageHeight))
                        {
                            n++;
                            var objectPoint = new RoomAliveToolkit.Matrix(3, 1);
                            objectPoint[0] = kinectCameraPoint.X;
                            objectPoint[1] = kinectCameraPoint.Y;
                            objectPoint[2] = kinectCameraPoint.Z;
                            objectPoints1.Add(objectPoint);

                            var colorPoint = new System.Drawing.PointF();
                            colorPoint.X = kinectColorPoint.X;
                            colorPoint.Y = kinectColorPoint.Y;
                            colorPoints1.Add(colorPoint);


                            //Console.WriteLine(objectPoint[0] + "\t" + objectPoint[1] + "\t" + colorPoint.X + "\t" + colorPoint.Y);

                            var depthPoint = new System.Drawing.PointF();
                            depthPoint.X = kinectDepthPoint.X;
                            depthPoint.Y = kinectDepthPoint.Y;
                            depthPoints1.Add(depthPoint);
                        }
                    }

            colorCameraMatrix[0, 0] = 1000; //fx
            colorCameraMatrix[1, 1] = 1000; //fy
            colorCameraMatrix[0, 2] = colorImageWidth / 2; //cx
            colorCameraMatrix[1, 2] = colorImageHeight / 2; //cy
            colorCameraMatrix[2, 2] = 1;

            var rotation = new Matrix(3, 1);
            var translation = new Matrix(3, 1);
            var colorError = CalibrateColorCamera(objectPoints1, colorPoints1, colorCameraMatrix, colorLensDistortion, rotation, translation);
            //var rotationMatrix = Orientation.Rodrigues(rotation);
            var rotationMatrix = RoomAliveToolkit.ProjectorCameraEnsemble.RotationMatrixFromRotationVector(rotation);

            depthToColorTransform = Matrix.Identity(4, 4);
            for (int i = 0; i < 3; i++)
            {
                depthToColorTransform[i, 3] = translation[i];
                for (int j = 0; j < 3; j++)
                    depthToColorTransform[i, j] = rotationMatrix[i, j];
            }


            depthCameraMatrix[0, 0] = 360; //fx
            depthCameraMatrix[1, 1] = 360; //fy
            depthCameraMatrix[0, 2] = depthImageWidth / 2; //cx
            depthCameraMatrix[1, 2] = depthImageHeight / 2; //cy
            depthCameraMatrix[2, 2] = 1;

            var depthError = CalibrateDepthCamera(objectPoints1, depthPoints1, depthCameraMatrix, depthLensDistortion);

            //// latest SDK gives access to depth intrinsics directly -- this gives slightly higher projection error; not sure why
            //var depthIntrinsics = kinectSensor.CoordinateMapper.GetDepthCameraIntrinsics();
            //depthCameraMatrix[0, 0] = depthIntrinsics.FocalLengthX;
            //depthCameraMatrix[1, 1] = depthIntrinsics.FocalLengthY;
            //depthCameraMatrix[0, 2] = depthIntrinsics.PrincipalPointX;
            //depthCameraMatrix[1, 2] = depthImageHeight - depthIntrinsics.PrincipalPointY; // note flip in Y!
            //depthDistCoeffs[0] = depthIntrinsics.RadialDistortionSecondOrder;
            //depthDistCoeffs[1] = depthIntrinsics.RadialDistortionFourthOrder;


            // check projections
            double depthProjectionError = 0;
            double colorProjectionError = 0;
            var color = new RoomAliveToolkit.Matrix(4, 1);
            var testObjectPoint4 = new RoomAliveToolkit.Matrix(4, 1);
            for (int i = 0; i < n; i++)
            {
                var testObjectPoint = objectPoints1[i];
                var testDepthPoint = depthPoints1[i];
                var testColorPoint = colorPoints1[i];

                // "camera space" == depth camera space
                // depth camera projection
                double depthU, depthV;
                CameraMath.Project(depthCameraMatrix, depthLensDistortion, testObjectPoint[0], testObjectPoint[1], testObjectPoint[2], out depthU, out depthV);

                double dx = testDepthPoint.X - depthU;
                double dy = testDepthPoint.Y - depthV;
                depthProjectionError += (dx * dx) + (dy * dy);

                // color camera projection
                testObjectPoint4[0] = testObjectPoint[0];
                testObjectPoint4[1] = testObjectPoint[1];
                testObjectPoint4[2] = testObjectPoint[2];
                testObjectPoint4[3] = 1;

                color.Mult(depthToColorTransform, testObjectPoint4);
                color.Scale(1.0 / color[3]); // not necessary for this transform

                double colorU, colorV;
                CameraMath.Project(colorCameraMatrix, colorLensDistortion, color[0], color[1], color[2], out colorU, out colorV);

                dx = testColorPoint.X - colorU;
                dy = testColorPoint.Y - colorV;
                colorProjectionError += (dx * dx) + (dy * dy);
            }
            depthProjectionError /= n;
            colorProjectionError /= n;


            stopWatch.Stop();
            Console.WriteLine("FakeCalibration :");
            Console.WriteLine("n = " + n);
            Console.WriteLine("color error = " + colorError);
            Console.WriteLine("depth error = " + depthError);
            Console.WriteLine("depth reprojection error = " + depthProjectionError);
            Console.WriteLine("color reprojection error = " + colorProjectionError);
            Console.WriteLine("depth camera matrix = \n" + depthCameraMatrix);
            Console.WriteLine("depth lens distortion = \n" + depthLensDistortion);
            Console.WriteLine("color camera matrix = \n" + colorCameraMatrix);
            Console.WriteLine("color lens distortion = \n" + colorLensDistortion);

            Console.WriteLine(stopWatch.ElapsedMilliseconds + " ms");


            //// get camera space table
            //// this does not change frame to frame (or so I believe)
            //var tableEntries = kinectSensor.CoordinateMapper.GetDepthFrameToCameraSpaceTable();

            //// compute our own version of the camera space table and compare it to the SDK's
            //stopWatch.Restart();

            //var tableEntries2 = ComputeDepthFrameToCameraSpaceTable();
            //Console.WriteLine("ComputeDepthFrameToCameraSpaceTable took " + stopWatch.ElapsedMilliseconds + " ms");

            //{
            //    float error = 0;
            //    for (int framey = 0; framey < depthImageHeight; framey++)
            //        for (int framex = 0; framex < depthImageWidth; framex++)
            //        {
            //            var point1 = tableEntries[depthImageWidth * framey + framex];
            //            var point2 = tableEntries2[depthImageWidth * framey + framex];

            //            error += (float)Math.Sqrt((point1.X - point2.X) * (point1.X - point2.X) + (point1.Y - point2.Y) * (point1.Y - point2.Y));
            //        }
            //    error /= (float)(depthImageHeight * depthImageWidth);
            //    Console.WriteLine("error = " + error);
            //}


        }



        public System.Drawing.PointF[] ComputeDepthFrameToCameraSpaceTable(int tableWidth = depthImageWidth, int tableHeight = depthImageHeight)
        {
            float fx = (float)depthCameraMatrix[0, 0];
            float fy = (float)depthCameraMatrix[1, 1];
            float cx = (float)depthCameraMatrix[0, 2];
            float cy = (float)depthCameraMatrix[1, 2];
            float[] kappa = new float[] { (float)depthLensDistortion[0], (float)depthLensDistortion[1] };

            var table = new System.Drawing.PointF[tableWidth * tableHeight];

            for (int y = 0; y < tableHeight; y++)
                for (int x = 0; x < tableWidth; x++)
                {
                    double xout, yout;
                    double framex = (double)x / (double)tableWidth * depthImageWidth;   // in depth camera image coordinates
                    double framey = (double)y / (double)tableHeight * depthImageHeight;

                    CameraMath.Undistort(fx, fy, cx, cy, kappa, framex, (depthImageHeight - framey), out xout, out yout);

                    var point = new System.Drawing.PointF();
                    point.X = (float)xout;
                    point.Y = (float)yout;
                    table[tableWidth * y + x] = point;
                }
            return table;
        }

        static double CalibrateDepthCamera(List<Matrix> worldPoints, List<System.Drawing.PointF> imagePoints, Matrix cameraMatrix, Matrix distCoeffs)
        {
            int nPoints = worldPoints.Count;

            // pack parameters into vector
            // parameters: fx, fy, cx, cy, k1, k2 = 6 parameters
            int nParameters = 6;
            var parameters = new Matrix(nParameters, 1);

            {
                int pi = 0;
                parameters[pi++] = cameraMatrix[0, 0]; // fx
                parameters[pi++] = cameraMatrix[1, 1]; // fy
                parameters[pi++] = cameraMatrix[0, 2]; // cx
                parameters[pi++] = cameraMatrix[1, 2]; // cy
                parameters[pi++] = distCoeffs[0]; // k1
                parameters[pi++] = distCoeffs[1]; // k2
            }

            // size of our error vector
            int nValues = nPoints * 2; // each component (x,y) is a separate entry

            LevenbergMarquardt.Function function = delegate(Matrix p)
            {
                var fvec = new Matrix(nValues, 1);

                // unpack parameters
                int pi = 0;
                double fx = p[pi++];
                double fy = p[pi++];
                double cx = p[pi++];
                double cy = p[pi++];
                double k1 = p[pi++];
                double k2 = p[pi++];

                var K = Matrix.Identity(3, 3);
                K[0, 0] = fx;
                K[1, 1] = fy;
                K[0, 2] = cx;
                K[1, 2] = cy;

                var d = Matrix.Zero(5, 1);
                d[0] = k1;
                d[1] = k2;

                int fveci = 0;
                for (int i = 0; i < worldPoints.Count; i++)
                {
                    // fvec_i = y_i - f(x_i)
                    double u, v;
                    var x = worldPoints[i];
                    CameraMath.Project(K, d, x[0], x[1], x[2], out u, out v);

                    var imagePoint = imagePoints[i];
                    fvec[fveci++] = imagePoint.X - u;
                    fvec[fveci++] = imagePoint.Y - v;
                }
                return fvec;
            };

            // optimize
            var calibrate = new LevenbergMarquardt(function);
            while (calibrate.State == LevenbergMarquardt.States.Running)
            {
                var rmsError = calibrate.MinimizeOneStep(parameters);
                Console.WriteLine("rms error = " + rmsError);
            }
            for (int i = 0; i < nParameters; i++)
                Console.WriteLine(parameters[i] + "\t");
            Console.WriteLine();

            // unpack parameters
            {
                int pi = 0;
                double fx = parameters[pi++];
                double fy = parameters[pi++];
                double cx = parameters[pi++];
                double cy = parameters[pi++];
                double k1 = parameters[pi++];
                double k2 = parameters[pi++];
                cameraMatrix[0, 0] = fx;
                cameraMatrix[1, 1] = fy;
                cameraMatrix[0, 2] = cx;
                cameraMatrix[1, 2] = cy;
                distCoeffs[0] = k1;
                distCoeffs[1] = k2;
            }


            return calibrate.RMSError;
        }

        static double CalibrateColorCamera(List<Matrix> worldPoints, List<System.Drawing.PointF> imagePoints, Matrix cameraMatrix, Matrix distCoeffs, Matrix rotation, Matrix translation)
        {
            int nPoints = worldPoints.Count;

            {
                Matrix R, t;
                CameraMath.DLT(cameraMatrix, distCoeffs, worldPoints, imagePoints, out R, out t);
                //var r = Orientation.RotationVector(R);
                var r = RoomAliveToolkit.ProjectorCameraEnsemble.RotationVectorFromRotationMatrix(R);
                rotation.Copy(r);
                translation.Copy(t);
            }

            // pack parameters into vector
            // parameters: fx, fy, cx, cy, k1, k2, + 3 for rotation, 3 translation = 12
            int nParameters = 12;
            var parameters = new Matrix(nParameters, 1);

            {
                int pi = 0;
                parameters[pi++] = cameraMatrix[0, 0]; // fx
                parameters[pi++] = cameraMatrix[1, 1]; // fy
                parameters[pi++] = cameraMatrix[0, 2]; // cx
                parameters[pi++] = cameraMatrix[1, 2]; // cy
                parameters[pi++] = distCoeffs[0]; // k1
                parameters[pi++] = distCoeffs[1]; // k2
                parameters[pi++] = rotation[0];
                parameters[pi++] = rotation[1];
                parameters[pi++] = rotation[2];
                parameters[pi++] = translation[0];
                parameters[pi++] = translation[1];
                parameters[pi++] = translation[2];

            }

            // size of our error vector
            int nValues = nPoints * 2; // each component (x,y) is a separate entry

            LevenbergMarquardt.Function function = delegate(Matrix p)
            {
                var fvec = new Matrix(nValues, 1);


                // unpack parameters
                int pi = 0;
                double fx = p[pi++];
                double fy = p[pi++];
                double cx = p[pi++];
                double cy = p[pi++];

                double k1 = p[pi++];
                double k2 = p[pi++];

                var K = Matrix.Identity(3, 3);
                K[0, 0] = fx;
                K[1, 1] = fy;
                K[0, 2] = cx;
                K[1, 2] = cy;

                var d = Matrix.Zero(5, 1);
                d[0] = k1;
                d[1] = k2;

                var r = new Matrix(3, 1);
                r[0] = p[pi++];
                r[1] = p[pi++];
                r[2] = p[pi++];

                var t = new Matrix(3, 1);
                t[0] = p[pi++];
                t[1] = p[pi++];
                t[2] = p[pi++];

                //var R = Orientation.Rodrigues(r);
                var R = RoomAliveToolkit.ProjectorCameraEnsemble.RotationMatrixFromRotationVector(r);



                var x = new Matrix(3, 1);

                int fveci = 0;
                for (int i = 0; i < worldPoints.Count; i++)
                {
                    // transform world point to local camera coordinates
                    x.Mult(R, worldPoints[i]);
                    x.Add(t);

                    // fvec_i = y_i - f(x_i)
                    double u, v;
                    CameraMath.Project(K, d, x[0], x[1], x[2], out u, out v);

                    var imagePoint = imagePoints[i];
                    fvec[fveci++] = imagePoint.X - u;
                    fvec[fveci++] = imagePoint.Y - v;
                }
                return fvec;
            };

            // optimize
            var calibrate = new LevenbergMarquardt(function);
            while (calibrate.State == LevenbergMarquardt.States.Running)
            {
                var rmsError = calibrate.MinimizeOneStep(parameters);
                Console.WriteLine("rms error = " + rmsError);
            }
            for (int i = 0; i < nParameters; i++)
                Console.WriteLine(parameters[i] + "\t");
            Console.WriteLine();

            // unpack parameters
            {
                int pi = 0;
                double fx = parameters[pi++];
                double fy = parameters[pi++];
                double cx = parameters[pi++];
                double cy = parameters[pi++];
                double k1 = parameters[pi++];
                double k2 = parameters[pi++];
                cameraMatrix[0, 0] = fx;
                cameraMatrix[1, 1] = fy;
                cameraMatrix[0, 2] = cx;
                cameraMatrix[1, 2] = cy;
                distCoeffs[0] = k1;
                distCoeffs[1] = k2;
                rotation[0] = parameters[pi++];
                rotation[1] = parameters[pi++];
                rotation[2] = parameters[pi++];
                translation[0] = parameters[pi++];
                translation[1] = parameters[pi++];
                translation[2] = parameters[pi++];
            }


            return calibrate.RMSError;
        }


    }
}
