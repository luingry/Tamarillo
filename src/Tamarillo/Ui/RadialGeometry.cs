using System;
using System.Windows;
using System.Windows.Media;

namespace Tamarillo.Ui
{
    /// <summary>
    /// Pure geometry of the wheel. Angles are degrees in screen space: 0 = right, 90 = down, increasing
    /// clockwise. Slot 0 is centred at the top (-90).
    /// </summary>
    public static class RadialGeometry
    {
        public static double SlotAngle(int count) => 360.0 / Math.Max(1, count);

        public static double SlotCenter(int index, int count) => -90 + index * SlotAngle(count);

        /// <summary>Normalizes to (-180, 180].</summary>
        public static double Normalize(double angle)
        {
            angle %= 360;
            if (angle <= -180)
            {
                angle += 360;
            }
            else if (angle > 180)
            {
                angle -= 360;
            }
            return angle;
        }

        public static double AngleOf(Vector v) => Math.Atan2(v.Y, v.X) * 180 / Math.PI;

        /// <summary>Slot under an angle for <paramref name="count"/> equal slots starting at the top.</summary>
        public static int SlotAt(double angle, int count)
        {
            double step = SlotAngle(count);
            double fromTop = Normalize(angle + 90 + step / 2);
            if (fromTop < 0)
            {
                fromTop += 360;
            }
            return (int)(fromTop / step) % count;
        }

        /// <summary>Centre angle of child <paramref name="index"/> in a sub-ring of <paramref name="count"/> items around <paramref name="groupAngle"/>.</summary>
        public static double SubItemCenter(double groupAngle, int index, int count, double itemAngle) =>
            groupAngle + (index - (count - 1) / 2.0) * itemAngle;

        /// <summary>Child under an angle, or -1 if the angle is outside the sub-ring (half an item of tolerance).</summary>
        public static int SubItemAt(double angle, double groupAngle, int count, double itemAngle)
        {
            double offset = Normalize(angle - groupAngle);
            double first = -(count / 2.0) * itemAngle;
            double position = (offset - first) / itemAngle;
            if (position < -0.5 || position >= count + 0.5)
            {
                return -1;
            }
            return Math.Max(0, Math.Min(count - 1, (int)Math.Floor(position)));
        }

        public static Point Polar(Point center, double radius, double angleDegrees)
        {
            double a = angleDegrees * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
        }

        /// <summary>
        /// Annular sector between <paramref name="startAngle"/> and <paramref name="endAngle"/> with a constant-width
        /// gap to its neighbours (<paramref name="gap"/> px) and rounded corners (<paramref name="corner"/> px).
        /// </summary>
        public static Geometry Sector(Point c, double inner, double outer, double startAngle, double endAngle, double gap, double corner)
        {
            double span = endAngle - startAngle;
            double halfGapOuter = Deg(gap / 2 / outer), halfGapInner = Deg(gap / 2 / inner);
            double a0o = startAngle + halfGapOuter, a1o = endAngle - halfGapOuter;
            double a0i = startAngle + halfGapInner, a1i = endAngle - halfGapInner;

            double k = Math.Max(0, Math.Min(corner, Math.Min((outer - inner) / 2 - 0.5, inner * Rad(a1i - a0i) / 2 - 0.5)));
            Point p1 = Polar(c, outer, a0o), p2 = Polar(c, outer, a1o), p3 = Polar(c, inner, a1i), p4 = Polar(c, inner, a0i);
            Vector u1 = p1 - p4;
            u1.Normalize();
            Vector u2 = p3 - p2;
            u2.Normalize();

            var figure = new PathFigure { IsClosed = true, StartPoint = p1 - u1 * k };
            figure.Segments.Add(new QuadraticBezierSegment(p1, Polar(c, outer, a0o + Deg(k / outer)), true));
            figure.Segments.Add(new ArcSegment(Polar(c, outer, a1o - Deg(k / outer)), new Size(outer, outer), 0, span > 180, SweepDirection.Clockwise, true));
            figure.Segments.Add(new QuadraticBezierSegment(p2, p2 + u2 * k, true));
            figure.Segments.Add(new LineSegment(p3 - u2 * k, true));
            figure.Segments.Add(new QuadraticBezierSegment(p3, Polar(c, inner, a1i - Deg(k / inner)), true));
            figure.Segments.Add(new ArcSegment(Polar(c, inner, a0i + Deg(k / inner)), new Size(inner, inner), 0, span > 180, SweepDirection.Counterclockwise, true));
            figure.Segments.Add(new QuadraticBezierSegment(p4, p4 + u1 * k, true));
            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            return geometry;
        }

        /// <summary>Arc used by the progress ring: from the top, clockwise, <paramref name="fraction"/> of a full turn.</summary>
        public static Geometry Arc(Point c, double radius, double fraction)
        {
            fraction = Math.Max(0, Math.Min(0.9999, fraction));
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(Polar(c, radius, -90), false, false);
                ctx.ArcTo(Polar(c, radius, -90 + 360 * fraction), new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static double Deg(double radians) => radians * 180 / Math.PI;

        private static double Rad(double degrees) => degrees * Math.PI / 180;
    }
}
