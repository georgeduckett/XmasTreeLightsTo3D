﻿using HSG.Numerics;
using Newtonsoft.Json;
using OpenCvSharp;
using TreeLightsWeb.Models;

namespace TreeLightsWeb.ImageProcessing
{
    public class ImageProcessor
    {
        private readonly ImageProcessingModel _model;

        private readonly Point[] Points;
        public ImageProcessor(ImageProcessingModel model)
        {
            _model = model;
            var imageBasePath = Path.Combine(model.WebRootFolder!, "CapturedImages");
            Points = Enumerable.Range(0, _model.LEDCount).Select(i => new Point(imageBasePath, i)).ToArray();
        }
        private string GetAverageImageFileName(int i)
        {
            return Path.Combine(Path.GetDirectoryName(Points[0].imagepath[0])!, $"average_{i * 45}.png");
        }
        public static MinMaxLocResult? GetMinMaxLoc(string imageFilePath)
        {
            if (File.Exists(imageFilePath[..imageFilePath.LastIndexOf('.')] + "_foundLoc.json"))
            {
                return JsonConvert.DeserializeObject<MinMaxLocResult>(File.ReadAllText($"{imageFilePath[..imageFilePath.LastIndexOf('.')]}_foundLoc.json"))!;
            }
            else
            {
                return null;
            }
        }
        private MinMaxLocResult ImageBP(string filePath, Mat averageImage, int imageAngleIndex)
        {
            if (!_model.RecalculateImageLEDCoordinates)
            {
                var existingResult = GetMinMaxLoc(filePath);
                if (existingResult != null)
                {
                    return existingResult;
                }
            }

            using var image = Cv2.ImRead(filePath);
            
            // Use the difference between the average image and the image to find the brightest point
            Cv2.Subtract(image, averageImage, image);
            using var gray = new Mat(); // The greyscale image
            using var masked = new Mat(); // THe masked version of the gray image
            image.CopyTo(gray);
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

            // Roughly blank out the left and right of the image, that isn't the tree, but gets lit up sometimes
            var leftBlank = _model.ImageMaskingModels![imageAngleIndex].LeftBlank;
            Cv2.Rectangle(gray, new Rect(0, 0, leftBlank, gray.Height), new Scalar(0), -1);
            var rightBlank = _model.ImageMaskingModels![imageAngleIndex].RightBlank;
            Cv2.Rectangle(gray, new Rect(rightBlank, 0, gray.Width - rightBlank, gray.Height), new Scalar(0), -1);

            // TODO: Use ideas in here maybe: https://pyimagesearch.com/2016/10/31/detecting-multiple-bright-spots-in-an-image-with-python-and-opencv/

            gray.CopyTo(masked);

            Cv2.GaussianBlur(masked, masked, new Size(_model.BlurAmount, _model.BlurAmount), 0);

            Cv2.MinMaxLoc(masked, out var minVal, out var maxVal, out var minLoc, out var maxLoc);

            if (maxVal < _model.BrightnessThreshold)
            {
                maxVal = 0;
            }
            else
            {
                // Only draw the circle if we're counting it
                Cv2.Circle(image, maxLoc, 41, new Scalar(0, 0, 255), 2);
            }
            Cv2.ImWrite($"{filePath[..filePath.LastIndexOf('.')]}_foundLoc.png", image);
            Cv2.ImWrite($"{filePath[..filePath.LastIndexOf('.')]}_masked.png", masked);

            var result = new MinMaxLocResult(minVal, maxVal, minLoc, maxLoc, new OpenCvSharp.Point(image.Width, image.Height));

            File.WriteAllText($"{filePath[..filePath.LastIndexOf('.')]}_foundLoc.json", JsonConvert.SerializeObject(result));

            return result;
        }
        private IEnumerable<IEnumerable<T>> CombinationsWithUpToXRemoved<T>(IEnumerable<T> items, int maxItemsToRemove)
        {
            // Return the collection with nothing removed
            yield return items;
            if (maxItemsToRemove <= 0) yield break;

            for (int i = 0; i < items.Count(); i++)
            {
                // Loop through the collection, recursing with one each one removed
                foreach (var newItems in CombinationsWithUpToXRemoved(items.Where((item, index) => index != i), maxItemsToRemove - 1))
                {
                    yield return newItems;
                }
            }
        }

        public async Task ProcessImages(Func<string, Task> updateFunc)
        {
            // Find average image
            using var firstImage = Cv2.ImRead(Points[0].imagepath[0]);
            using var accMask = new Mat();

            for (var i = 0; i < Points[0].imagepath.Length; i++)
            {
                using var acc = Mat.Zeros(new Size(firstImage.Width, firstImage.Height), MatType.CV_64FC3).ToMat();
                foreach (var point in Points)
                {
                    await updateFunc($"\rFind average of {i * 45} degree images. {point.index} of {Points.Length}");
                    using var img = Cv2.ImRead(point.imagepath[i]);
                    Cv2.Accumulate(img, acc, accMask);
                }

                Cv2.Divide(acc, new Scalar(Points.Length, Points.Length, Points.Length), acc);
                Cv2.ImWrite(GetAverageImageFileName(i), acc);
            }

            var averageImages = Enumerable.Range(0, Points[0].imagepath.Length).Select(i => Cv2.ImRead(GetAverageImageFileName(i))).ToArray();

            // Find brightest points
            foreach (var point in Points)
            {
                await updateFunc($"\rFind brightest points. {point.index} of {Points.Length}");
                for (var i = 0; i < point.imagepath.Length; i++)
                {
                    var minMax = ImageBP(point.imagepath[i], averageImages[i], i);
                    point.ImageX[i] = minMax.MaxLoc.X;
                    point.ImageY[i] = minMax.MaxLoc.Y;
                    point.ImageWeight[i] = minMax.MaxVal;
                    
                }

                if (point.ImageWeight.All(w => w == 0))
                {
                    throw new Exception("Found an LED with no detectable position from any angle.");
                }

                // TODO: Check the Y values, and discount any that aren't close to the average.
                // Work out the (unweighted) average
                var averageY = point.ImageY.Sum() / point.ImageY.Where(y => y != 0).Count();


                var iyOrderOfDeltaFromAvg = Enumerable.Range(0, point.ImageY.Length)
                                                      .OrderByDescending(i => Math.Abs(point.ImageY[i] - averageY))
                                                      .Select(i => i);

                foreach (var iyIndex in iyOrderOfDeltaFromAvg)
                {
                    if (point.ImageWeight[iyIndex] == 0) continue; // We've already discounted this one, so just carry on
                                                                   // Always have at least 4 points
                    if (point.ImageWeight.Count(w => w != 0) <= _model.MinPointsToKeep) break;
                    if (Math.Abs(point.ImageY[iyIndex] - averageY) <= _model.MaxYDiffBetweenTreeRotations) break; // If we're within 20 of the average that's fine
                    point.ImageWeight[iyIndex] = 0;
                    // TODO: Maybe have the routine circle the found pixel now, so we can choose not to here

                    // TODO: Maybe another way of doing this is when equation solving, try removing some points to see if we get a much closer solution to the equations

                }

                // Make the weights add up to one
                var weightsSum = point.ImageWeight.Sum();

                for (var i = 0; i < point.ImageWeight.Length; i++)
                {
                    point.ImageWeight[i] /= weightsSum;
                }
            }

            // Find the average Ys
            foreach (var point in Points)
            {
                await updateFunc($"\rSet average Ys. {point.index} of {Points.Length}");
                point.OriginalTreeZ = point.WeightedAverageYs();
            }

            // Adjust all x values so the origin is the average of Xs in all images in a given tree rotation
            for (int i = 0; i < Points[0].ImageX.Length; i++)
            {
                var averageX = Points.Average(p => p.ImageX[i] * p.ImageWeight[i]);

                foreach (var point in Points)
                {
                    point.ImageX[i] -= averageX;
                }
            }

            // Adjust all the Y values so the origin is the max Y (bottom of image as origin in OpenCV is top left) and +ve y's go up
            var maxY = Points.Max(p => p.OriginalTreeZ);
            foreach (var point in Points)
            {
                point.OriginalTreeZ = maxY - point.OriginalTreeZ;
            }

            // Solve the equations to go from x position on image and tree rotation, an r and theta (radius and angle?)
            foreach (var point in Points)
            {
                await updateFunc($"\rSolve equations. {point.index} of {Points.Length}");
                var weightedEquations = point.Equations.Zip(point.ImageWeight)
                                                 .Select(pair => new { Equation = pair.First, Weight = pair.Second })
                                                 .Where(pair => pair.Weight != 0) // Ignore equations with a weighting of zero
                                                 .ToArray();

                List<Tuple<double[], double[], string>> possibleSolutions = [];

                //var maxToRemove = weightedEquations.Length - 4; // Remove down to a minimum of 4 equations
                var maxToRemove = 0; // Don't remove any equations, just use them all (as it doesn't seem to make much difference we just leave it at none

                foreach (var equationCombinations in CombinationsWithUpToXRemoved(weightedEquations, maxToRemove))
                {
                    // Just try doing it in one big list of equations (except where weighting is zero). Note that this doesn't use the weighting beyond that though
                    double[] AllFuncsToSolve(double[] variables) => weightedEquations.Select(we => we.Equation(variables[0], variables[1])).ToArray();

                    possibleSolutions.Add(Fsolve.Fsolver(AllFuncsToSolve, 2, [400.0, 0.0], 1e-10));
                }

                // Find the combination of equations that gives the lowest delta, and use that solution
                (var solutionsAll, var bestEquationResultsAll, var infoAll) = possibleSolutions.OrderBy(result => result.Item2.Select(Math.Abs).Average()).First();

                point.r = solutionsAll[0];
                point.theta = solutionsAll[1];
                point.EquationSolverDelta = bestEquationResultsAll.Select(Math.Abs).Average();
            }


            // Calculate the Tree Coords of each led
            foreach (var point in Points)
            {
                point.OriginalTreeX = point.r!.Value * Math.Cos(point.theta!.Value);
                point.OriginalTreeY = point.r!.Value * Math.Sin(point.theta!.Value);
                // Point.TreeZ already set
            }


            // Iterate through all points to work out what ones are obviously wrong, given the average distance between LEDs and divide them between probably ok LEDs

            // Find distance between points
            for (int i = 0; i < Points.Length - 1; i++)
            {
                Points[i + 1].DistanceBefore = Points[i].DistanceAfter =
                    Math.Sqrt((Points[i].OriginalTreeX - Points[i + 1].OriginalTreeX) * (Points[i].OriginalTreeX - Points[i + 1].OriginalTreeX) +
                              (Points[i].OriginalTreeY - Points[i + 1].OriginalTreeY) * (Points[i].OriginalTreeY - Points[i + 1].OriginalTreeY) +
                              (Points[i].OriginalTreeZ - Points[i + 1].OriginalTreeZ) * (Points[i].OriginalTreeZ - Points[i + 1].OriginalTreeZ));
            }


            var probablyCorrectDistancePixels = Points.OrderBy(p => p.DistanceBefore).Take((int)(_model.LEDCount * _model.ProportionToAssumeCorrectDistances)).ToArray();
            var averageDistance = probablyCorrectDistancePixels.Average(p => p.DistanceBefore);
            var maxSeparation = averageDistance / 0.75; // 3/4 is the average distance within a unit sphere

            foreach (var point in Points)
            {
                point.DistanceAboveThreshold = point.DistanceAfter > maxSeparation || point.DistanceBefore > maxSeparation;
                // Start of with them being the same
                point.CorrectedTreeX = point.OriginalTreeX;
                point.CorrectedTreeY = point.OriginalTreeY;
                point.CorrectedTreeZ = point.OriginalTreeZ;
            }

            // Go through and mark for correction any where before it and after it are to be corrected
            for (int i = 1; i < Points.Length - 1; i++)
            {
                if (Points[i - 1].DistanceAboveThreshold && Points[i + 1].DistanceAboveThreshold)
                {
                    Points[i].DistanceAboveThreshold = true;
                }
            }


            var nextGoodIndex = 0;
            var currentIndex = 0;

            if (Points[currentIndex].DistanceAboveThreshold)
            { // First point is wrong, so make it and subsequent wrong ones the same as the first correct one
                while (Points[nextGoodIndex].DistanceAboveThreshold) nextGoodIndex++;
                for (; currentIndex < nextGoodIndex; currentIndex++)
                {
                    Points[currentIndex].CorrectedTreeX = Points[nextGoodIndex].CorrectedTreeX;
                    Points[currentIndex].CorrectedTreeY = Points[nextGoodIndex].CorrectedTreeY;
                    Points[currentIndex].CorrectedTreeZ = Points[nextGoodIndex].CorrectedTreeZ;
                }
            }

            var nextGoodIndexBackwards = Points.Length - 1;
            var currentIndexBackwards = Points.Length - 1;
            if (Points[currentIndexBackwards].DistanceAboveThreshold)
            { // Last point is wrong, so make it and subsequent wrong ones the same as the first previous correct one
                while (Points[nextGoodIndexBackwards].DistanceAboveThreshold) nextGoodIndexBackwards--;
                for (; currentIndexBackwards > nextGoodIndexBackwards; currentIndexBackwards--)
                {
                    Points[currentIndexBackwards].CorrectedTreeX = Points[nextGoodIndexBackwards].CorrectedTreeX;
                    Points[currentIndexBackwards].CorrectedTreeY = Points[nextGoodIndexBackwards].CorrectedTreeY;
                    Points[currentIndexBackwards].CorrectedTreeZ = Points[nextGoodIndexBackwards].CorrectedTreeZ;
                }
            }

            // Now go from nextGoodIndex to nextGoodIndexBackwards to do the rest
            for (; currentIndex < currentIndexBackwards; currentIndex++)
            {
                if (Points[currentIndex].DistanceAboveThreshold)
                {
                    nextGoodIndex = currentIndex;
                    while (Points[nextGoodIndex].DistanceAboveThreshold) nextGoodIndex++;

                    var previousGoodIndex = currentIndex - 1;

                    var xDiffStep = (Points[nextGoodIndex].CorrectedTreeX - Points[previousGoodIndex].CorrectedTreeX) / (nextGoodIndex - previousGoodIndex);
                    var yDiffStep = (Points[nextGoodIndex].CorrectedTreeY - Points[previousGoodIndex].CorrectedTreeY) / (nextGoodIndex - previousGoodIndex);
                    var zDiffStep = (Points[nextGoodIndex].CorrectedTreeZ - Points[previousGoodIndex].CorrectedTreeZ) / (nextGoodIndex - previousGoodIndex);

                    for (; currentIndex < nextGoodIndex; currentIndex++)
                    {
                        Points[currentIndex].CorrectedTreeX = Points[previousGoodIndex].CorrectedTreeX + (currentIndex - previousGoodIndex) * xDiffStep;
                        Points[currentIndex].CorrectedTreeY = Points[previousGoodIndex].CorrectedTreeY + (currentIndex - previousGoodIndex) * yDiffStep;
                        Points[currentIndex].CorrectedTreeZ = Points[previousGoodIndex].CorrectedTreeZ + (currentIndex - previousGoodIndex) * zDiffStep;
                    }

                    currentIndex = nextGoodIndex;
                }
            }

            // Convert to GIFT coordinates - Make X and Y go from -ve 1 to 1, and Z go from 0 up (using the scale of the max of X and Y_
            var xMin = Points.Min(p => p.CorrectedTreeX);
            var xMax = Points.Max(p => p.CorrectedTreeX);
            var yMin = Points.Min(p => p.CorrectedTreeY);
            var yMax = Points.Max(p => p.CorrectedTreeY);
            var zMin = Points.Min(p => p.CorrectedTreeZ);

            foreach (var point in Points)
            {
                point.GiftX = (point.CorrectedTreeX - xMin) / ((xMax - xMin) / 2) - 1;
                point.GiftY = (point.CorrectedTreeY - yMin) / ((yMax - yMin) / 2) - 1;
                point.GiftZ = (point.CorrectedTreeZ - zMin) / (Math.Max(xMax - xMin, yMax - yMin) / 2);
            } // We divide GiftZ by two since the X and Y values go from -1 to 1 (which is 2 long)


            // Now write the csv
            File.WriteAllLines(Path.Combine(_model.WebRootFolder!, "coordinates.csv"), new[] { "index, x, y, z, r, theta, equdelta, wascorrected, original x, original y, original z" }
                .Concat(Points.Select(p => $"{p.index}, {p.GiftX}, {p.GiftY}, {p.GiftZ}, {p.r}, {p.theta}, {p.EquationSolverDelta}, {p.DistanceAboveThreshold}, {p.OriginalTreeX}, {p.OriginalTreeY}, {p.OriginalTreeZ}")));


            await updateFunc(string.Empty);
            await updateFunc($"Found points to correct with indexes: {string.Join(", ", Points.Where(p => p.DistanceAboveThreshold).Select(p => p.index))}");
            await updateFunc($"Done, with {Points.Count(p => !p.DistanceAboveThreshold) / (double)Points.Count():P2} probably correct points and an equation solver delta sum of {Points.Sum(p => p.EquationSolverDelta)} and average of {Points.Average(p => p.EquationSolverDelta)}");
        }
    }
}
