namespace Comet.Core.Mathematics
{
    public static class Rotations
    {
        public static uint RotateLeft(this uint value, int count) =>
            (value << count % 32) | (value >> (32 - count % 32));
        
        public static uint RotateRight(this uint value, int count) =>
            (value >> count % 32) | (value << (32 - count % 32));
    }
}
