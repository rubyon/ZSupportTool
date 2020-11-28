namespace ZSupport
{
    class LinearEquation
    {
        public static double getY(double x1, double y1, double x2, double y2, double x)
        {
            return (y2 - y1) / (x2 - x1) * (x - x1) + y1;
        }

        public static double getX(double x1, double y1, double x2, double y2, double y)
        {
            return (y - y1) * ((x2 - x1) / (y2 - y1)) + x1;
        }
    }
}
