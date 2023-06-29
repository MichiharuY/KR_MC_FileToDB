using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KAISYOU
{
    /// <summary>
    /// 初期設定読み込みクラス（陸排通信用）
    /// </summary>
    public static class clsPrmFile
    {
        public static string m_FileName = "";      // ファイル名(フルパス)
        public static string m_ErrorMessage = "";  // エラーメッセージ
        public static List<string> m_PrmFileData = new List<string>(); // パラメータファイルの内容

        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public static string ErrorMessage
        {
            get { return m_ErrorMessage; }
        }

        /// <summary>
        /// ファイル名(フルパス)
        /// </summary>
        public static string FileName
        {
            get { return m_FileName; }
            set { m_FileName = value; }
        }
        
        /// <summary>
        /// ファイルの読み出し
        /// </summary>
        /// <returns></returns>
        public static bool LoadPrmFile()
        {
            try
            {
                // ファイルの読み出し+解析
                using ( TextFieldParser parser = new TextFieldParser(m_FileName, Encoding.GetEncoding("Shift_JIS")) )
                {
                    parser.TextFieldType = FieldType.Delimited;
                    parser.SetDelimiters(","); // 区切り文字はコンマ
                    parser.HasFieldsEnclosedInQuotes = true;    // 引用符を考慮する

                    m_PrmFileData.Clear();

                    // 1行ずつ読み込みList<short>のテーブルにデータを格納する
                    while ( !parser.EndOfData )
                    {
                        string[] row = parser.ReadFields();
                        if ( row.Length >= 2 )
                        {
                            m_PrmFileData.Add(row[1]);
                        }
                    }
                }
                return true;
            }
            catch ( Exception exp )
            {
                Debug.WriteLine(exp.StackTrace);
                Debug.WriteLine(exp.Message);
                m_ErrorMessage = "clsPrmFileクラス -> Load エラー";
                return false;
            }
        }
        
        /// <summary>
        /// パラメータの収得
        /// </summary>
        /// <param name="FilePath">ファイル名(フルパス)</param>
        /// <param name="PrmData">パラメータデータ</param>
        /// <returns>True=正常,False=異常</returns>
        public static bool GetPrmData(string FilePath, ref List<string> PrmData )
        {
            bool ret = false;

            // パラメータファイルの読み出し
            m_FileName = FilePath;
            ret = LoadPrmFile();

            if ( ret == true )
            {
                PrmData = m_PrmFileData;
                return true;
            }
            else
            {
                return false;
            }
        }

    }
}
