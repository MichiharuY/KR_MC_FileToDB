using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Data.SqlClient;
using System.Windows.Threading;

/// <summary>
/// NET10アドレスの区分Index
/// </summary>
enum NET10_DIV
{
    DTMaster = 0,
    Net10Data,
}

namespace _02_NET10収録_FILEtoDB_
{
    // イベント
    public delegate void DBConnectionEventHandler(bool status, string info);  // データベースへの接続処理結果を返す
    public delegate void TargetDBEventHandler(string dbName, string groupName);  // このグループの対象データベース名イベント
    public delegate void RecFileCountEventHandler(int fileCount);  // 区分毎のファイル数と、対象データベース名を返す。
    public delegate void DBInsertOtherErrorEventHandler(string error);  // 
    public delegate void StartBulkInsertEventHandler(int divNo);  // 区分別処理開始イベント
    public delegate void NotifyBulkInsertInfoEventHandler(int divNo, int count, string finfo);  // 区分別処理途中経過イベント
    public delegate void EndBulkInsertEventHandler(int divNo);  // 区分別処理完了イベント
    public delegate void BulkInsertErrorEventHandler(int divNo, string error, string command);  // 

    //********************************************************************************
    // データベース制御クラス
    //********************************************************************************
    public class DBInsert
    {
        // イベント
        public event DBConnectionEventHandler DBConnection;  // データベースへの接続処理結果を返す
        public event RecFileCountEventHandler RecFileCount;  // 区分毎のファイル数と、対象データベース名を返す。
        public event TargetDBEventHandler TargetDB;  // このグループの対象データベース名イベント
        public event DBInsertOtherErrorEventHandler DBInsertOtherError;  // 
        public event StartBulkInsertEventHandler StartBulkInsert;  // 区分別処理開始イベント
        public event NotifyBulkInsertInfoEventHandler NotifyBulkInsertInfo;  // 区分別処理途中経過イベント
        public event EndBulkInsertEventHandler EndBulkInsert;  // 区分別処理完了イベント
        public event BulkInsertErrorEventHandler BulkInsertError;  // 

        // 定数
        /// <summary>収録フォルダ名（NET10->FILEの転送先フォルダ）</summary>
        public const string DATADIR = "F2DB";
        /// <summary>
        /// <para>問題があった収録ファイルの格納フォルダ名</para>
        /// <para>1つでも収録ファイルがかけていた場合はこちらに移動される</para>
        /// </summary>
        public const string ERRDIR_GROUP = "ERRGRP";
        /// <summary>
        /// <para>問題があった収録ファイルの格納フォルダ名</para>
        /// <para>DBアクセス処理（BulkInsert）近辺で例外が発生した場合はこちらに移動される</para>
        /// </summary>
        public const string ERRDIR_INSERT = "ERRINSERT";

        /// <summary>BulkInsert処理に失敗したファイル（×を付ける候補のファイル）</summary>
        private const string NGNAME = "△";
        /// <summary>BulkInsert処理に失敗したファイル（エラーフォルダに移動する）</summary>
        private const string ERRNAME = "×";

        // DB接続文字列
        //const string CONNECT_STRING = "Integrated Security=SSPI;Persist Security Info=False;User ID=sa;Connection Timeout=10";
        const string CONNECT_STRING = "Integrated Security=SSPI;Persist Security Info=False;Connection Timeout=10";
        //const string CONNECT_STRING = "PROVIDER=SQLOLEDB;Integrated Security=SSPI;Persist Security Info=False;User ID=sa";

        const int COMMAND_TIMEOUT = 10; // SQLコマンドのタイムアウト時間

        /// <summary>収録先DBのテーブル名</summary>
        static readonly string[] DB_TABLENAME = { "DTMaster", "Net10Master" };

        /// <summary>収録フォルダ名</summary>
        static readonly string[] DIVNAME = { "DTMaster", "Net10Data" };

        // 変数
        /// <summary>収録フォルダパス</summary>
        public string m_DataDir = "";
        /// <summary>問題があった収録ファイルの格納フォルダパス（全区分の収録ファイルが揃っていない）</summary>
        public string m_GroupErrDir = "";
        /// <summary>問題があった収録ファイルの格納フォルダパス（BulkInsertに失敗した）</summary>
        public string m_InsertErrDir = "";

        private SqlConnection m_SQLConnect; // SQL接続クラス（ADO.NET）
        private SqlCommand m_SQLCommand;    // SQLコマンドクラス（ADO.NET）
        private bool m_IsConnect = false;   // true : DBに接続している

        //********************************************************************************
        // コンストラクタ
        //********************************************************************************
        public DBInsert()
        {

        }

        //********************************************************************************
        // デストラクタ
        //********************************************************************************
        ~DBInsert()
        {
            // DB切断処理
            disconnectDataBase();
        }

        //********************************************************************************
        // フォルダパスの設定
        //********************************************************************************
        public bool setFolderPath(string folderPath)
        {
            m_DataDir = folderPath + DATADIR;
            m_GroupErrDir = folderPath + ERRDIR_GROUP;
            m_InsertErrDir = folderPath + ERRDIR_INSERT;

            if (Directory.Exists(folderPath) == false)
                return false;

            // エラーフォルダがなかったら作成しておく
            string dirName = "";

            foreach (string divName in DIVNAME)
            {
                dirName = m_GroupErrDir + "\\" + divName;
                if (Directory.Exists(dirName) == false)
                    Directory.CreateDirectory(dirName);

                dirName = m_InsertErrDir + "\\" + divName;
                if (Directory.Exists(dirName) == false)
                    Directory.CreateDirectory(dirName);
            }

            return true;
        }

        //********************************************************************************
        // データベースへの接続
        //********************************************************************************
        public bool connectDataBase()
        {
            try
            {
                // データベースへ接続
                m_SQLConnect = new SqlConnection();
                m_SQLConnect.ConnectionString = CONNECT_STRING;
                m_SQLConnect.Open();

                // SQL送信クラスの初期化
                m_SQLCommand = new SqlCommand();
                m_SQLCommand.Connection = m_SQLConnect;
                m_SQLCommand.CommandTimeout = COMMAND_TIMEOUT;

                DBConnection(true, "SQL Serverに接続しました。");

                m_IsConnect = true; // 接続フラグON

#if DEBUG
                Debug.WriteLine("Connect database");
#endif
            }
            catch (Exception exp)
            {
                // エラー通知
                DBConnection(false, exp.Message);
                disconnectDataBase();
                return false;
            }

            return true;
        }

        //********************************************************************************
        // データベースから切断
        //********************************************************************************
        public bool disconnectDataBase()
        {
            if (m_SQLConnect != null)
            {
                m_SQLConnect.Close();
                m_SQLConnect.Dispose();
                m_SQLConnect = null;
            }

            if (m_SQLCommand != null)
            {
                m_SQLCommand.Dispose();
                m_SQLCommand = null;
            }

            m_IsConnect = false; // 接続フラグOFF

#if DEBUG
            Debug.WriteLine("Disconnect database");
#endif

            return true;
        }

        //********************************************************************************
        // データベースに収録データをBulkInsertする
        // DTMasterに対応するファイルグループの作成。（９ファイルでOK。その他はNG）
        // ここで作成したファイルグループがBulkInsert処理対象。
        //********************************************************************************
        public void execBulkInsert()
        {
            NET10_DIV div = NET10_DIV.DTMaster;

            string[] baseFiles;         // 収録対象ファイル。このファイル名から同一時刻の他の区分のファイルを見つける
            string[] recFilePath = new string[(int)NET10_DIV.Net10Data + 1];    // 収録対象ファイル（同一時刻の各区分のファイル）
            string recTimeName = "";    // 収録対象時刻
            string dbName = "";         // 収録DB名（月毎に違うDB）
            int insertCount = 0;        // 収録件数

            // BulkInsert処理
            string execFileName = "";   // 収録ファイル名
            string execCommand;         // BulkInsertコマンド文字列

            bool success = false;       // true : BulkInsertに成功した

            string tmp = "";
            int index = 0;

            // 2018/12/13 ファイルを掴まれていて収録できない現象が発生したため収録方法変更
            //          （DTMasterのファイルが2つ以上コピーさている状態で、最新を残して収録処理を行う）※必ず最新の1つのファイルが残る
            try
            {
                ////////////////////////////////////////
                // 未接続の場合は再接続
                if (m_IsConnect == false)
                {
                    if (connectDataBase() == false)
                        return;
                }

                ////////////////////////////////////////
                // 収録対象ファイルの取得（DTMasterが基準）
                baseFiles = Directory.GetFiles(m_DataDir + "\\" + DIVNAME[(int)NET10_DIV.DTMaster], "*", System.IO.SearchOption.TopDirectoryOnly);

                RecFileCount(baseFiles.Length - 1);  // 収録ファイル数の通知（イベント）

                if (baseFiles.Length > 1)
                {
                    var baseFilesList = new List<string>();
                    baseFilesList.AddRange(baseFiles);
                    baseFilesList.Sort();

                    for (int i = 0; i < baseFilesList.Count - 1; i++)
                    {
                        insertCount++;

                        ////////////////////////////////////////
                        // 同一時刻の収録ファイルの探索
                        // DTMasterのファイルパスの格納
                        recFilePath[(int)NET10_DIV.DTMaster] = baseFilesList[i];

                        tmp = Path.GetFileName(recFilePath[(int)NET10_DIV.DTMaster]);   // ファイル名取得
                        index = tmp.IndexOf("DTMaster");    // DTMasterの位置を検索

                        if (index < 0) // 見つからない
                            continue;

                        // 収録時刻をDTMasterのファイル名より取得
                        recTimeName = tmp.Substring(index + 8, 14);

                        // ファイル名から対象データベース名を取得
                        dbName = "KR" + tmp.Substring(index + 8, 6);

                        TargetDB(dbName, recTimeName);    // フォームに収録時刻、対象DB名を通知（イベント）

                        ////////////////////////////////////////
                        // 他区分の収録ファイルの探索
                        bool canRecording = true;
                        for (div = NET10_DIV.DTMaster; div <= NET10_DIV.Net10Data; div++)
                        {
                            // 検索フォルダ、検索文字列
                            string searchFolder = m_DataDir + "\\" + DIVNAME[(int)div];
                            string searchStr = "*" + recTimeName + "*";

                            // 収録ファイルの検索
                            string[] searchFiles = Directory.GetFiles(searchFolder, searchStr, System.IO.SearchOption.TopDirectoryOnly);

                            // 収録ファイルが見つかったらパスを格納、無ければエラーとする
                            if (searchFiles.Length != 0)
                            {
                                recFilePath[(int)div] = searchFiles[0];
                            }
                            else
                            {
                                recFilePath[(int)div] = "";
                                canRecording = false;   // 1区分でも見つからなかったら収録しない
                            }
                        }

                        ////////////////////////////////////////
                        if (canRecording == true)   // 収録可能
                        {
                            ////////////////////////////////////////
                            // DBへNET10データを収録する
                            for (div = NET10_DIV.DTMaster; div <= NET10_DIV.Net10Data; div++)
                            {
                                try
                                {
                                    StartBulkInsert((int)div); // BulkInsert開始通知（イベント）

                                    //メッセージキューに現在あるWindowsメッセージをすべて処理する
                                    //DoEvents();

                                    // 収録ファイルパスの取得
                                    execFileName = recFilePath[(int)div];

                                    // SQLコマンドの作成（BULK INSERT）
                                    execCommand = "BULK INSERT " + dbName + ".." + DB_TABLENAME[(int)div] + " FROM '" + execFileName + "' WITH (DATAFILETYPE = 'char', FIELDTERMINATOR = ',', ROWTERMINATOR = '\n',ORDER (RecID ASC))";
                                    m_SQLCommand.CommandText = execCommand;
                                    
                                    // SQL実行
                                    m_SQLCommand.ExecuteNonQuery();

                                    // 収録ファイルの削除
                                    File.Delete(recFilePath[(int)div]);

                                    NotifyBulkInsertInfo((int)div, insertCount, Path.GetFileName(execFileName)); // BulkInsert処理内容通知（イベント）
                                    EndBulkInsert((int)div); // BulkInsert終了通知（イベント）

                                    //メッセージキューに現在あるWindowsメッセージをすべて処理する
                                    DoEvents();

                                    success = true;
                                }
                                catch (Exception e)
                                {
                                    // 失敗した場合は、ファイル名の先頭に△を付ける
                                    moveErrorFile(recFilePath[(int)div], m_DataDir, (int)div, NGNAME);

                                    // エラー通知
                                    BulkInsertError((int)div, e.Message, execFileName);
                                }
                            }
                        }
                        else
                        {
                            ////////////////////////////////////////
                            // エラー処理
                            for (div = NET10_DIV.DTMaster; div <= NET10_DIV.Net10Data; div++)
                            {
                                // 全区分そろっていない収録ファイルをエラーフォルダへ移動
                                if (recFilePath[(int)div] != "")
                                    moveErrorFile(recFilePath[(int)div], m_GroupErrDir, (int)div, ERRNAME);
                            }

                            // エラー通知
                            DBInsertOtherError("ファイルグループが作成できませんでした。");
                        }
                    }
                }

                ////////////////////////////////////////
                // 一度でもBulkInsretに成功した場合は、残っている△のファイルは収録ファイルに問題があるものとみなしERRINSERTフォルダに移動する
                if (success == true)
                {
                    for (div = NET10_DIV.DTMaster; div <= NET10_DIV.Net10Data; div++)
                    {
                        string[] errFiles = Directory.GetFiles(m_DataDir + "\\" + DIVNAME[(int)div], NGNAME + "*", System.IO.SearchOption.TopDirectoryOnly);

                        foreach (string file in errFiles)
                            moveErrorFile(file, m_InsertErrDir, (int)div, ERRNAME);
                    }
                }
                else
                {
                    // エラーの場合は一旦切断する
                    disconnectDataBase();
                }
            }
            catch (Exception e)
            {
                // エラー通知
                BulkInsertError((int)div, e.Message, execFileName);

                // エラーの場合は一旦切断する
                disconnectDataBase();
            }
        }

        //********************************************************************************
        // 収録に問題があったファイルをエラーフォルダに移動する
        // ファイル名の先頭にheaderNameがつきます。
        //********************************************************************************
        public void moveErrorFile(string srcPath, string dstDir, int div, string headerName)
        {
            if (File.Exists(srcPath) == true)
                File.Move(srcPath, dstDir + "\\" + DIVNAME[div] + "\\" + headerName + Path.GetFileName(srcPath).Trim('△'));
        }

        /// <summary>
        /// メッセージ キューに現在ある Windows メッセージをすべて処理する(WPFでのDoEvents)
        /// </summary>
        public void DoEvents()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(ExitFrames), frame);
            Dispatcher.PushFrame(frame);
        }

        public object ExitFrames(object f)
        {
            ((DispatcherFrame)f).Continue = false;

            return null;
        }
    }
}
