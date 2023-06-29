using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace KAISYOU
{
    //********************************************************************************
    // アプリケーションのログ出力クラス
    //********************************************************************************
    public class LogFile
    {
        public string filePath;

        //********************************************************************************
        // コンストラクタ
        //********************************************************************************
        public LogFile()
        {
            filePath = "LogFile.log";
        }

        public LogFile(string path)
        {
            filePath = path;
        }

        //********************************************************************************
        // ログ出力
        //********************************************************************************
        public bool SaveLog(string logStr)
        {
            try
            {
                using (StreamWriter file = new StreamWriter(filePath, true, Encoding.GetEncoding("shift_jis")))
                    file.WriteLine(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss:fff : ") + logStr);
            }
            catch
            {
                return false;
            }

            return true;
        }
    }
}
