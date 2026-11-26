namespace Fetih.Core.Mathematics
{
    using System;

    public static class Swaps
    {
        public static void Swap(this ref uint n1, ref uint n2)
        {
            uint tmp = n1;
            n1 = n2;
            n2 = tmp;
        }
    }
}
