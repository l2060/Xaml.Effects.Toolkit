using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Xaml.Effects.Toolkit.Uitity
{
    public class ImeHelper
    {
        /// <summary>
        /// ImmGetContext
        /// </summary>
        /// <param name="hWnd"></param>
        [DllImport("Imm32.dll")]
        public static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("Imm32.dll", CharSet = CharSet.Unicode)]
        private static extern int ImmGetCompositionStringW(IntPtr hIMC, int dwIndex, byte[] lpBuf, int dwBufLen);
        [DllImport("imm32.dll", CharSet = CharSet.Auto)]
        public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll", CharSet = CharSet.Auto)]
        public static extern bool ImmSetCompositionString(IntPtr hIMC, int dwIndex, byte[] lpComp, int dwCompLen, byte[] lpRead, int dwReadLen);
        [DllImport("imm32.dll", CharSet = CharSet.Auto)]
        public static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);
        private const int GCS_COMPSTR = 0x0008;
        private const int SCS_SETSTR = 0x0009;
        ///// <summary>
        ///// Get IME Candidate Text
        ///// </summary>
        ///// <param name="sender">The Active TextBox</param>
        ///// <param name="maxtext">The Maxlength Of TextBox</param>
        //public void GetImeTextFun(object sender, int maxtext)
        //{
        // TextBox textbox = sender as TextBox;
        // IntPtr hIMC = ImmGetContext(textbox.Handle);
        // int strLen = ImmGetCompositionStringW(hIMC, GCS_COMPSTR, null, 0);
        // string addtext = null;
        // int count = 0;
        // if (strLen > 0)
        // {
        // byte[] buffer = new byte[strLen];
        // int k = ImmGetCompositionStringW(hIMC, GCS_COMPSTR, buffer, strLen);
        // UnicodeEncoding converter = new UnicodeEncoding();
        // int cursor = textbox.SelectionStart;
        // string text = textbox.Text;
        // string pretext = text.Substring(0, cursor);
        // //Console.WriteLine("{0},岝?埵抲{1}", pretext,cursor);
        // string latertext = text.Substring(cursor, text.Length - cursor);
        // //Console.WriteLine("{0},岝?埵抲{1}", latertext, cursor);
        // string input = converter.GetString(buffer);
        // //Console.WriteLine("{0}", input);
        // foreach (char a in input)
        // {
        // if (maxtext - text.Length - count > 0)
        // {
        // addtext += a;
        // }
        // count++;
        // }
        // textbox.Text = pretext + addtext + latertext;
        // ImmSetOpenStatus(hIMC, false);
        // ImmSetCompositionString(hIMC, SCS_SETSTR, null, 0, null, 0);
        // ImmSetOpenStatus(hIMC, true);
        // ImmReleaseContext(textbox.Handle, hIMC);
        // }
        //}
        public static void CancelIMEWindow(IntPtr hwnd)
        {
            IntPtr hIMC = ImeHelper.ImmGetContext(hwnd);
            ImmSetOpenStatus(hIMC, true);
            ImmSetOpenStatus(hIMC, false);
            ImeHelper.ImmReleaseContext(hwnd, hIMC);
        }

        /// <summary>
        /// Get IME Candidate Text
        /// </summary>
        /// <param name="sender">The Active TextBox</param>
        /// <param name="maxtext">The MaxByteLength Of TextBox</param>
        public static void GetImeTextByteLength(IntPtr hwnd)
        {
            IntPtr hIMC = ImmGetContext(hwnd);
            int strLen = ImmGetCompositionStringW(hIMC, GCS_COMPSTR, null, 0);
            //string addtext = null;
            if (strLen > 0)
            {
                byte[] buffer = new byte[strLen];
                ImmGetCompositionStringW(hIMC, GCS_COMPSTR, buffer, strLen);
                //       int text_maxbyte = Encoding.Default.GetByteCount(textbox.Text);
                UnicodeEncoding converter = new UnicodeEncoding();
                //  int cursor = textbox.SelectionStart;
                //  string text = textbox.Text;
                //    string pretext = text.Substring(0, cursor);
                //Console.WriteLine("{0},岝?埵抲{1}", pretext,cursor);
                //     string latertext = text.Substring(cursor, text.Length - cursor);
                //Console.WriteLine("{0},岝?埵抲{1}", latertext, cursor);
                string input = converter.GetString(buffer);
                //int maxbyte = 0;
                //Console.WriteLine("{0}", input);
                //foreach (char a in input)
                //{
                //    if (maxtext - text_maxbyte - maxbyte > 1)
                //    {
                //        addtext += a;
                //        maxbyte = Encoding.Default.GetByteCount(addtext);
                //    }
                //}
                //textbox.Text = pretext + addtext + latertext;
                ImmSetOpenStatus(hIMC, false);
                ImmSetCompositionString(hIMC, SCS_SETSTR, null, 0, null, 0);
                ImmSetOpenStatus(hIMC, true);
                ImmReleaseContext(hwnd, hIMC);
            }
        }
    }
}
