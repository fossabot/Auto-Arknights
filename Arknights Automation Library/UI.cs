﻿using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Text;
using REVUnit.Crlib;
using REVUnit.Crlib.Extension;
using REVUnit.ImageLocator;
using Point = System.Drawing.Point;

namespace REVUnit.AutoArknights.Core
{
    public class UI : IDisposable
    {
        private readonly Adb _adb;
        private readonly FMLocator _loc = new FMLocator(new Feature2DInfo(Feature2Ds.Sift));
        private readonly OCRTesseract _tesseract = OCRTesseract.Create("Assets\\Tesseract", "eng");

        public UI(string adbPath)
        {
            _adb = new Adb(adbPath);
        }

        public UI(string adbPath, string adbRemote)
        {
            _adb = new Adb(adbPath);
            NewRemote(adbRemote);
        }

        // public async Task<Point> Loc(string text)
        // {
        //     var result = await BaiduOcr.FindWithLoc(Adb.GetScreenShot());
        //     if (result.words_result_num == 0)
        //     {
        //         return new Point(-1, -1);
        //     }
        //     List<dynamic> wordsResult = result.words_result;
        //     var matched =
        //         wordsResult.Where(wordResult =>
        //             wordResult.words == text);
        //     foreach (var x in wordsResult)
        //     {
        //         Console.WriteLine(x.words);
        //     }
        //     if (!matched.Any())
        //     {
        //         return (default, false); //没有找到对应的
        //     }
        //     var location = matched.First().location;
        //     return (
        //         new Point((int) location.left + (int) location.width / 2,
        //             (int) location.top + (int) location.height / 2), true);
        //     //返回文字的中心点
        // }
        public void Dispose()
        {
            _loc.Dispose();
            _tesseract.Dispose();
            _adb.Dispose();
        }

        public void NewRemote(string adbRemote)
        {
            _adb.Connect(adbRemote);
        }

        private T Ocr<T>(Func<Mat, Mat> src, string regex, TryParser<string[], T> tryParser, TimeSpan waitSpan)
        {
            return X.While(() =>
            {
                using Mat scrn = Scrn();
                using Mat sub = src(scrn);
                Cv2.ImShow("subimg", sub);
                Cv2.WaitKey();
                dynamic[] wordsResult = BaiduOcr.Ocr(sub).Result.words_result.ToArray();
                dynamic[] words = wordsResult.Select(it => it.words).ToArray();
                //_tesseract.Run(sub, out string str, out _, out _, out _);
                return Regex.Match((string) words[0], regex);
            }, match =>
            {
                T result = default;
                return (match.Success && tryParser(match.Groups.Values.Select(it => it.Value).ToArray(), out result),
                    result);
            }, waitSpan);
        }

        public int GetRequiredSanity()
        {
            return Ocr(GetRequiredSanityPart, @"\d+",
                (string[] arr, out int result) => int.TryParse(arr[0], out result), TimeSpan.FromSeconds(1));
        }

        private Mat GetRequiredSanityPart(Mat super)
        {
            var sanityRect = new Rect((int) (super.Width * 0.927), (int) (super.Height * 0.941),
                (int) (super.Width * 0.035), (int) (super.Height * 0.035));
            return GetPart(super, sanityRect);
        }

        public Sanity GetCurrentSanity()
        {
            return Ocr(GetCurrentSanityPart, @"(\d+)\/(\d+)", (string[] arr, out Sanity result) =>
            {
                int[] ints = arr.SelectCanParse<string, int>(int.TryParse).ToArray();
                bool valid = ints.Length == 2;
                result = valid ? new Sanity(ints[0], ints[1]) : default;
                return valid;
            }, TimeSpan.FromSeconds(1));
        }

        private static Mat GetPart(Mat super, Rect rect)
        {
            return super.Clone(rect);
        }

        private static Mat GetCurrentSanityPart(Mat super)
        {
            var sanityRect = new Rect((int) (super.Width * 0.88), (int) (super.Height * 0.02),
                (int) (super.Width * 0.1), (int) (super.Height * 0.08));
            return GetPart(super, sanityRect);
        }

        public void Slp(double sec)
        {
            Task.Delay(TimeSpan.FromSeconds(sec)).Wait();
        }

        private Mat Scrn()
        {
            return X.While(_adb.GetScreenShot, result => !result.Empty());
        }

        public void WaitAp(string expr, double durationSec = 1.75)
        {
            X.While(() => Loc(expr), result => result.Succeed, TimeSpan.FromSeconds(durationSec));
        }

        public bool TestAp(string expr)
        {
            return Loc(expr).Succeed;
        }

        public LocateResult Loc(string expr)
        {
            using Mat model = Asset(expr);
            return Loc(model);
        }

        public LocateResult Loc(Mat model)
        {
            using Mat scrn = Scrn();
            return _loc.Locate(model, scrn);
        }

        public void Clk(string asset)
        {
            Clk(Asset(asset));
        }

        public void Clk(Mat model)
        {
            Clk(X.While(() => Loc(model), result => result.Succeed).CenterPoint);
        }

        public void Clk(int x, int y)
        {
            Clk(new Point(x, y));
        }

        public void Clk(Point point)
        {
            _adb.Click(Randomize(point));
        }

        private static Mat Asset(string expr)
        {
            string fileName = Path.Combine("Assets",
                                  string.Join('\\', expr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))) +
                              ".png";
            if (!File.Exists(fileName)) throw new IOException("Invalid asset expression");
            var mat = new Mat(fileName);
            if (mat.Empty()) throw new IOException($"Invalid asset file {fileName}");
            return mat;
        }

        private static Point Randomize(Point point)
        {
            return new Point(Math.Abs(RandomNumberGenerator.GetInt32(-3, 3) + point.X),
                Math.Abs(RandomNumberGenerator.GetInt32(-3, 3) + point.Y));
        }
    }
}