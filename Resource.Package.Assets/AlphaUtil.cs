namespace Resource.Package.Assets
{
    public class AlphaUtil
    {
        public static unsafe void UnpremultiplyAlpha(byte[] data)
        {
            fixed (byte* ptr = &data[0])
            {
                byte* ptr2 = ptr;
                int num = 0;
                while (num < data.Length)
                {
                    float alpha = ptr2[3] / 255f;
                    if (alpha != 0) // 避免除以0
                    {
                        *ptr2 = (byte)(*ptr2 / alpha);
                        ptr2[1] = (byte)(ptr2[1] / alpha);
                        ptr2[2] = (byte)(ptr2[2] / alpha);
                    }
                    num += 4;
                    ptr2 += 4;
                }
            }
        }

        public static unsafe void PremultiplyAlpha(byte[] data)
        {
            fixed (byte* ptr = &data[0])
            {
                byte* ptr2 = ptr;
                int num = 0;
                while (num < data.Length)
                {
                    float num2 = (float)(int)ptr2[3] / 255f;
                    *ptr2 = (byte)((float)(int)(*ptr2) * num2);
                    ptr2[1] = (byte)((float)(int)ptr2[1] * num2);
                    ptr2[2] = (byte)((float)(int)ptr2[2] * num2);
                    num += 4;
                    ptr2 += 4;
                }
            }
        }

        public static unsafe void SwitchRedBlue(byte[] data)
        {
            fixed (byte* ptr = &data[0])
            {
                byte* ptr2 = ptr;
                int num = 0;
                while (num < data.Length)
                {
                    var a = ptr2[2];
                    ptr2[2] = ptr2[0];
                    *ptr2 = a;

                    num += 4;
                    ptr2 += 4;
                }
            }
        }


    }
}
