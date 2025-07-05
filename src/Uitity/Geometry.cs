using System;
using System.Collections.Generic;
using System.Windows;

namespace Xaml.Effects.Toolkit.Uitity
{
    /// <summary>
    /// 几何帮助类
    /// </summary>
    public class Geometry
    {
        #region pubic ENUM
        /// <summary>
        /// 方向
        /// </summary>
        public enum Direction
        {
            /// <summary>
            /// 顺时针
            /// </summary>
            Clockwise,
            /// <summary>
            /// 逆时针
            /// </summary>
            AntiClockwise
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// 根据两点计算弧的圆心
        /// </summary>
        /// <param name="p1">坐标1</param>
        /// <param name="p2">坐标2</param>
        /// <param name="dRadius">半径</param>
        /// <returns>圆心位置</returns>
        public static Point[] Circle_Center(Point p1, Point p2, double dRadius)
        {
            Double dDistance = System.Math.Sqrt((p1.X - p2.X) * (p1.X - p2.X) + (p1.Y - p2.Y) * (p1.Y - p2.Y));
            if (dDistance == 0.0)
            {
                throw new Exception("输入了相同的点");
            }
            if ((2 * dRadius) < dDistance)
            {
                throw new Exception("两点间距离大于直径");
            }
            Point center1 = new Point();
            Point center2 = new Point();
            double k = (p2.Y - p1.Y) / (p2.X - p1.X);
            if (k == 0)
            {
                center1.X = (p1.X + p2.X) / 2.0;
                center2.X = (p1.X + p2.X) / 2.0;
                center1.Y = p1.Y + System.Math.Sqrt(dRadius * dRadius - (p1.X - p2.X) * (p1.X - p2.X) / 4.0);
                center2.Y = p2.Y - System.Math.Sqrt(dRadius * dRadius - (p1.X - p2.X) * (p1.X - p2.X) / 4.0);
            }
            else
            {
                double k_verticle = -1.0 / k;
                double mid_X = (p1.X + p2.X) / 2.0;
                double mid_Y = (p1.Y + p2.Y) / 2.0;
                double a = 1.0 + k_verticle * k_verticle;
                double b = -2 * mid_X - k_verticle * k_verticle * (p1.X + p2.X);
                double c = mid_X * mid_X + k_verticle * k_verticle * (p1.X + p2.X) * (p1.X + p2.X) / 4.0 - (dRadius * dRadius - ((mid_X - p1.X) * (mid_X - p1.X) + (mid_Y - p1.Y) * (mid_Y - p1.Y)));

                center1.X = (-1.0 * b + System.Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
                center2.X = (-1.0 * b - System.Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
                center1.Y = Y_Coordinates(mid_X, mid_Y, k_verticle, center1.X);
                center2.Y = Y_Coordinates(mid_X, mid_Y, k_verticle, center2.X);
            }
            return new Point[] { center1, center2 };
        }

        /// <summary>
        /// 获取一段圆弧的路径
        /// </summary>
        /// <param name="a">点A</param>
        /// <param name="b">点B</param>
        /// <param name="radius">半径</param>
        /// <returns></returns>
        public static List<Point> GetArcPath(Point a, Point b, Double radius, Direction direction)
        {
            var centers = Circle_Center(b, a, radius);
            if (direction == Direction.Clockwise)
            {
                var center = centers[0];
                var sa = angle_360(a, center);
                var ea = angle_360(b, center);
                return GetArcPath(center, radius, sa, ea, true);
            }
            else
            {
                var center = centers[1];
                var sa = angle_360(b, center);
                var ea = angle_360(a, center);
                return GetArcPath(center, radius, sa, ea, true);
            }
        }

        /// <summary>
        /// 从直线上获取两点之间所有点
        /// </summary>
        /// <param name="pStart">起点</param>
        /// <param name="pEnd">终点</param>
        /// <param name="precision">精度</param>
        /// <returns></returns>
        public static List<Point> GetLinePoints(Point pStart, Point pEnd, Byte precision = 2)
        {
            //定义线上的点
            List<Point> linePoint = new List<Point>();
            if (pStart.X == pEnd.X && pStart.Y == pEnd.Y)
            {
                return linePoint;
            }
            //初始精度0步进
            Double stepnumber = 1;
            //生成精度步进
            if (precision > 0)
            {
                for (int i = 0; i < precision; i++)
                {
                    stepnumber /= 10;
                }
            }

            if (pStart.X == pEnd.X)
            {
                //起点X=终点X 没有斜率
                stepnumber = (pEnd.Y - pStart.Y > 0) ? stepnumber : -stepnumber;
                Double y = pStart.Y;
                while (y != pEnd.Y)
                {
                    linePoint.Add(new Point(pStart.X, y));
                    y = System.Math.Round(y + stepnumber, precision);
                }
                linePoint.Add(new Point(pEnd.X, pEnd.Y));
            }
            else if (pStart.Y == pEnd.Y)
            {
                //起点Y = 终点Y
                stepnumber = (pEnd.X - pStart.X > 0) ? stepnumber : -stepnumber;
                Double x = pStart.X;
                while (x != pEnd.X)
                {
                    linePoint.Add(new Point(x, pStart.Y));
                    x = System.Math.Round(x + stepnumber, precision);
                }
                linePoint.Add(new Point(pEnd.X, pEnd.Y));
            }
            else
            {
                //计算出直线的斜率
                double k = (pStart.Y - pEnd.Y) / (pStart.X - pEnd.X);
                //计算出步进
                stepnumber = (pEnd.X - pStart.X > 0) ? stepnumber : -stepnumber;
                Double x = pStart.X;
                Double c = 0;
                while (true)
                {
                    // 根据斜率,计算y坐标
                    double y = System.Math.Round(k * c + pStart.Y, precision);
                    linePoint.Add(new Point(x, y));
                    x = System.Math.Round(x + stepnumber, precision);
                    c = System.Math.Round(c + stepnumber, precision);
                    if (y == pEnd.Y || x == pEnd.X)
                    {
                        if (y != pEnd.Y || x != pEnd.X)
                        {
                            linePoint.Add(pEnd);
                        }
                        break;
                    }
                }
            }
            return linePoint;
        }

        /// <summary>
        /// 获取一段圆弧的路径
        /// </summary>
        /// <param name="center">圆中心</param>
        /// <param name="radius">圆半径</param>
        /// <param name="angleStart">开始角度</param>
        /// <param name="sweepAngle">结束角度</param>
        /// <param name="isUseDegree">标识angle是否为角度还是弧度</param>
        /// <returns></returns>
        public static List<Point> GetArcPath(Point center, double radius, double angleStart, double sweepAngle, Boolean isUseDegree)
        {
            List<Point> result = new List<Point>();
            double r = radius;//半径
            double ts = angleStart;//起始角度
            double te = sweepAngle;//终止角度
            double xc = center.X;//圆心x
            double yc = center.Y;//圆心y

            if (isUseDegree)//如果使用的是度数转化为弧度
            {
                ts = ts / 180 * System.Math.PI;
                te = te / 180 * System.Math.PI;
            }
            //设置步进长度规则
            double deg = (0);
            if (r < 5.08) deg = 0.015;
            else if (r < 7.62) deg = 0.06;
            else if (r < 25.4) deg = 0.075;
            else deg = 0.015;
            double dte = deg * 25.4 / r;
            if (te < ts)//终止角＜起始角
                te += System.Math.PI * 2;//PI = 3.1415;
            int nCount = (int)((te - ts) / dte + 0.5);
            double ta = ts;
            Double x = xc + r * System.Math.Cos(ts);
            Double y = yc + r * System.Math.Sin(ts);
            //dc.MoveTo(x, y);
            result.Add(new Point(x, y));
            for (int i = 1; i <= nCount; i++)
            {
                ta += dte;
                var ct = System.Math.Cos(ta);
                var st = System.Math.Sin(ta);
                x = xc + r * ct;
                y = yc + r * st;
                //dc.LineTo(x, y);
                result.Add(new Point(x, y));
            }
            x = xc + r * System.Math.Cos(te);
            y = yc + r * System.Math.Sin(te);
            //dc.LineTo(x, y);
            result.Add(new Point(x, y));
            return result;
        }

        #endregion

        #region Private Methods



        /// <summary>
        /// 两点之间的角度
        /// </summary>
        /// <param name="p1"></param>
        /// <param name="cc"></param>
        /// <param name="r"> 半径</param>
        /// <returns></returns>
        private static Double angle_360(Point p1, Point cc, Double r)
        {
            return System.Math.Acos((p1.X - cc.X) / r) * 180d / System.Math.PI;
        }

        /// <summary>
        /// 两点之间的角度
        /// </summary>
        /// <param name="from_"></param>
        /// <param name="to_"></param>
        /// <returns></returns>
        private static Double angle_360(Point from_, Point to_)
        {
            //两点的x、y值
            Double x = from_.X - to_.X;
            Double y = from_.Y - to_.Y;

            //斜边长度
            Double hypotenuse = System.Math.Sqrt(System.Math.Pow(x, 2f) + System.Math.Pow(y, 2f));

            //求出弧度
            Double cos = x / hypotenuse;
            Double radian = System.Math.Acos(cos);

            //用弧度算出角度    
            Double angle = 180 / (System.Math.PI / radian);

            if (y < 0)
            {
                angle = -angle;
            }
            else if ((y == 0) && (x < 0))
            {
                angle = 180;
            }
            return angle;
        }

        private static double Y_Coordinates(double x, double y, double k, double x0)
        {
            return k * x0 - k * x + y;
        }

        #endregion
    }
}
