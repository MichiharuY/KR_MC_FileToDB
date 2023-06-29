using KAISYOU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace _02_NET10収録_FILEtoDB_
{
    // 収録情報UI
    // 「Recording info」の各UIに区分Indexでアクセスするために
    // 下記構造体配列を最初に作成してます
    public struct UIRecInfo
    {
        public Label title;
        public TextBlock totalFileCount;
        public TextBlock fileCount;
        public TextBlock fileName;
        public ProgressBar progress;

        // 収録情報UIのセット
        public void setRecInfo(Label _title, TextBlock _totalFileCount, TextBlock _fileCount, TextBlock _fileName, ProgressBar _progress)
        {
            // UIをメンバ変数に設定
            title = _title;
            totalFileCount = _totalFileCount;
            fileCount = _fileCount;
            fileName = _fileName;
            progress = _progress;

            // UIの初期化
            totalFileCount.Text = "0";
            fileCount.Text = "0";
            fileName.Text = "";
            fileCount.Text = "0";
            progress.Value = 0;
        }
    }

    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        // 定数
        static readonly string LOGNAME = "FILEtoDB.log";    // ログファイル名

        static readonly int CONNECTWAITCOUNT = 4;           // DBへの接続試行回数
        static readonly int RECWAITTIME = 60;               // DBに収録する時間間隔（x1s）

        // 変数
        public LogFile m_Log;                               // ログ出力クラス
        public DBInsert m_DB;                               // DB収録クラス

        // 初期設定
        public string m_RecFolder = "";                     // 収録データ格納フォルダ（F2DB）

        // 「Recording info」の各UI
        public UIRecInfo[] m_UI = new UIRecInfo[(int)NET10_DIV.Net10Data + 1];

        public int m_ConnectCount = 0;                      // 接続に失敗した回数
        public int m_RecWaitCount = 0;                      // 最後にDBに収録してからの時間（x1s）

        public bool m_AllowAppExit = false;

        private DispatcherTimer timerDBConnect;             // DB接続タイマー
        private DispatcherTimer timerDBInsert;              // DB接続タイマー
        private DispatcherTimer timerProgress1;             // プログレスバークリアタイマー
        private DispatcherTimer timerProgress2;             // プログレスバークリアタイマー

        public string ExePath;                              // 実行ファイルPath

        public MainWindow()
        {
            InitializeComponent();

            ExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            ExePath = ExePath.Substring(0, ExePath.LastIndexOf("\\"));
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Window初期表示位置設定
            this.Top = 0;
            this.Left = 60;

            ////////////////////////////////////////
            // ログ出力の初期化
            m_Log = new LogFile(LOGNAME);

            ////////////////////////////////////////
            // 初期設定の読み込み
            if (LoadPrmFile() == false)
            {
                m_AllowAppExit = true;
                Close();
                return;
            }

            ////////////////////////////////////////
            // UI初期化
            controlInit();

            ////////////////////////////////////////
            // タイトル変更
            // タイトル変更のために必要な情報の取得（アセンブリ情報、実行ファイル情報）
            var asm = Assembly.GetExecutingAssembly();
            var fileInfo = new FileInfo(ExePath);

            // タイトル変更（実行ファイル更新日、バージョン情報を追加）
            this.Title += string.Format(" Build:{0} Ver:{1}", fileInfo.LastWriteTime.ToString("yyyyMMdd"), asm.GetName().Version);

            ////////////////////////////////////////
            // データベース制御クラスの初期化
            m_DB = new DBInsert();
            m_DB.DBConnection += new DBConnectionEventHandler(frmMain_DBConnection);
            m_DB.RecFileCount += new RecFileCountEventHandler(frmMain_RecFileCount);
            m_DB.TargetDB += new TargetDBEventHandler(frmMain_TargetDB);
            m_DB.DBInsertOtherError += new DBInsertOtherErrorEventHandler(frmMain_DBInsertOtherError);
            m_DB.StartBulkInsert += new StartBulkInsertEventHandler(frmMain_StartBulkInsert);
            m_DB.NotifyBulkInsertInfo += new NotifyBulkInsertInfoEventHandler(frmMain_NotifyBulkInsertInfo);
            m_DB.EndBulkInsert += new EndBulkInsertEventHandler(frmMain_EndBulkInsert);
            m_DB.BulkInsertError += new BulkInsertErrorEventHandler(frmMain_BulkInsertError);

            ////////////////////////////////////////
            // フォルダ設定の初期化
            if (initFolderPath() == false)
            {
                MessageBox.Show("ルートパス設定異常\r\n" + "\"" + m_DB.m_DataDir + "\"" + "が見つかりません。\r\n"
                    + "設定ファイルを確認して下さい。",
                    "収録フォルダーエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                m_AllowAppExit = true;
                Close();
                return;
            }

            ////////////////////////////////////////
            // BD接続タイマーを設定
            timerDBConnect = new DispatcherTimer(DispatcherPriority.Normal);
            timerDBConnect.Interval = new TimeSpan(0, 0, 1);    //接続間隔
            timerDBConnect.Tick += new EventHandler(timerDBConnect_Tick);

            // DB収録処理タイマーを設定
            timerDBInsert = new DispatcherTimer(DispatcherPriority.Normal);
            timerDBInsert.Interval = new TimeSpan(0, 0, 1);     //DB収録処理間隔
            timerDBInsert.Tick += new EventHandler(timerDBInsert_Tick);
            timerDBInsert.Start();

            // プログレスバークリアタイマーを設定
            timerProgress1 = new DispatcherTimer(DispatcherPriority.Normal);
            timerProgress1.Interval = new TimeSpan(0, 0, 3);    //MAX表示待機期間
            timerProgress1.Tick += new EventHandler(timerProgress1_Tick);

            timerProgress2 = new DispatcherTimer(DispatcherPriority.Normal);
            timerProgress2.Interval = new TimeSpan(0, 0, 3);    //MAX表示待機期間
            timerProgress2.Tick += new EventHandler(timerProgress2_Tick);

            ////////////////////////////////////////
            // DB接続タイマーを開始
            timerDBConnect.Start();

            return;

        }

        /// <summary>
        /// 終了前処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (m_AllowAppExit == false)
            {
                MessageBoxResult ret;

                // 終了確認メッセージを表示
                ret = MessageBox.Show("終了すると収録が停止します。よろしいですか？", "ユーザによる終了",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                if (ret == MessageBoxResult.Cancel)
                {
                    // アプリ終了を中断する
                    e.Cancel = true;
                    return;
                }
            }
        }

        /// <summary>
        /// 終了処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closed(object sender, EventArgs e)
        {
            // タイマー停止
            timerDBConnect.Stop();
            timerDBInsert.Stop();

            // DBとの接続を停止
            m_DB.disconnectDataBase();

            m_Log.SaveLog("FILEtoDB：プログラム正常終了");
        }

        /// <summary>
        /// パラメータファイルの読み出し
        /// </summary>
        /// <returns>True=正常,False=異常</returns>
        private bool LoadPrmFile()
        {
            string PrmFilePath = "";
            List<string> PrmData = null;

            // パラメータファイルの読み出し 
            PrmFilePath = Directory.GetCurrentDirectory() + @"\PrmFiles\System.ini";
            if (clsPrmFile.GetPrmData(PrmFilePath, ref PrmData) == true)
            {
                m_RecFolder = PrmData[0];

                return true;
            }
            else
            {
                string exeName = System.IO.Path.GetFileName(ExePath);
                // パラメータファイル読み出し不正
                MessageBox.Show(exeName + "のパラメータファイル読み出しに失敗しました。\r\n" +
                            "「" + PrmFilePath + "」を確認して下さい。", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// フォルダパス設定の初期化
        /// </summary>
        /// <returns></returns>
        public bool initFolderPath()
        {
            string path;

            // 設定ファイルから収録フォルダのパスを取得
            path = m_RecFolder;

            // DBクラスに設定しフォルダの存在確認
            if (m_DB.setFolderPath(path) == false)
                return false;

            if (Directory.Exists(m_DB.m_DataDir) == true)
                lbRecFolder.Text = m_DB.m_DataDir;   // 収録フォルダを表示
            else
                return false;

            return true;
        }

        /// <summary>
        /// DB接続タイマーイベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void timerDBConnect_Tick(object sender, EventArgs e)
        {
            // DB接続タイマーを停止
            timerDBConnect.Stop();

            stlbInfo.Text = "データベースへ接続中...";

            // データベースへ接続（接続イベントが発生します）
            if (m_DB.connectDataBase() == true)
            {
                timerDBInsert.Start();  // DB収録処理開始

                stlbInfo.Text = "データベース接続完了。";
                m_Log.SaveLog("FILEtoDB：DB収録処理開始");
            }
            else
            {
                // 接続失敗
                m_ConnectCount++;
                if (m_ConnectCount < CONNECTWAITCOUNT)
                {
                    // 既定回数までは再接続を試みる
                    string errStr = "10秒後にデータベースへ再接続します : " + m_ConnectCount.ToString() + "回目";
                    stlbInfo.Text = errStr;
                    m_Log.SaveLog(errStr);

                    timerDBConnect.Interval = new TimeSpan(0, 0, 10);    //接続間隔(10秒)
                    timerDBConnect.Start();
                }
                else
                {
                    // 既定回数失敗した場合はエラー終了
                    string errStr = "データベースへ接続できない為、プログラムの起動は中止されます。";
                    MessageBox.Show(errStr, "データベース接続処理", MessageBoxButton.OK, MessageBoxImage.Error);
                    m_Log.SaveLog(errStr);
                    m_AllowAppExit = true;
                    Close();
                }
            }

            return;
        }

        //********************************************************************************
        // DB収録タイマーイベント
        //********************************************************************************
        private void timerDBInsert_Tick(object sender, EventArgs e)
        {
            // 既定回数タイマーが呼ばれたらDBにデータを収録する
            if (m_RecWaitCount >= RECWAITTIME)
            {
                // BulkInsertが終了するまでタイマー停止
                timerDBInsert.Stop();

                // BulkInsert処理の開始
                m_DB.execBulkInsert();

                m_RecWaitCount = 0;

                // タイマー再開
                timerDBInsert.Start();
            }
            else
            {
                m_RecWaitCount++;
            }

            // 収録を行うまでの残り時間を表示する
            stlbRecWaitCount.Text = m_RecWaitCount.ToString();
        }

        //********************************************************************************
        // プログレスバークリアタイマーイベント
        //********************************************************************************
        private void timerProgress1_Tick(object sender, EventArgs e)
        {
            timerProgress1.Stop();
            m_UI[0].progress.Value = 0; // プログレスバー値を0に更新
        }

        private void timerProgress2_Tick(object sender, EventArgs e)
        {
            timerProgress2.Stop();
            m_UI[1].progress.Value = 0; // プログレスバー値を0に更新
        }

        //********************************************************************************
        //********************************************************************************
        // DBInsertからのイベント
        //********************************************************************************
        //********************************************************************************
        /// <summary>
        /// データベースへの接続処理結果を返す
        /// </summary>
        /// <param name="status"></param>
        /// <param name="info"></param>
        private void frmMain_DBConnection(bool status, string info)
        {
            if (status == false)
                txtError.Text = info;    // エラー発生の場合はエラー情報を表示
            else
                txtError.Text = "";
        }

        /// <summary>
        /// 収録ファイル数の通知（区分毎）
        /// </summary>
        /// <param name="fileCount"></param>
        private void frmMain_RecFileCount(int fileCount)
        {
            // ここは、BulkInsert処理の最初で来るので、エラー情報はすべてクリアしておく。
            txtError.Text = "";

            // 区分毎の収録情報の表示
            for (int i = 0; i <= (int)NET10_DIV.Net10Data; i++)
            {
                m_UI[i].totalFileCount.Text = fileCount.ToString(); // 収録ファイル数を表示
                m_UI[i].fileCount.Text = "0";
                m_UI[i].progress.Value = 0;

                if (fileCount != 0)
                    m_UI[i].progress.Maximum = fileCount;   // プログレスバーの最大値の変更
            }
        }

        /// <summary>
        /// 対象データベース名、ファイル名の通知
        /// </summary>
        /// <param name="dbName"></param>
        /// <param name="groupName"></param>
        private void frmMain_TargetDB(string dbName, string groupName)
        {
            // UIに表示
            lbTargetDB.Text = dbName;
            lbTargetDate.Text = groupName;
        }

        /// <summary>
        /// その他エラー通知（現状は収録ファイルが見つからない区分があった場合に通知される）
        /// </summary>
        /// <param name="error"></param>
        private void frmMain_DBInsertOtherError(string error)
        {
            // UIに表示
            txtError.Text = error;
        }

        /// <summary>
        /// BulkInsertを開始した区分を通知
        /// </summary>
        /// <param name="divNo"></param>
        private void frmMain_StartBulkInsert(int divNo)
        {
            if (divNo > (int)NET10_DIV.Net10Data)
                return;

            // 対象区分が分かるよう色を変更
            m_UI[divNo].title.Foreground = new SolidColorBrush(Colors.Orange);
        }

        /// <summary>
        /// 転送ファイルの情報を通知
        /// </summary>
        /// <param name="divNo"></param>
        /// <param name="count"></param>
        /// <param name="finfo"></param>
        private void frmMain_NotifyBulkInsertInfo(int divNo, int count, string finfo)
        {
            if (divNo > (int)NET10_DIV.Net10Data)
                return;

            try
            {
                m_UI[divNo].fileCount.Text = count.ToString();  // 処理済みファイル数を表示
                m_UI[divNo].progress.Value = count; // プログレスバー更新

                if (m_UI[divNo].progress.Value == m_UI[divNo].progress.Maximum) {
                    // プログレスバークリアタイマー開始
                    switch (divNo)
                    {
                        case 0:
                            timerProgress1.Start();
                            break;
                        case 1:
                            timerProgress2.Start();
                            break;
                    }
                }

                lbTargetFileName.Text = finfo;  // 転送ファイル名表示

                // 区分毎の表示はファイルの日時部分のみ表示
                string name;
                name = finfo.Substring(finfo.Length - 17 - 1, 14);
                m_UI[divNo].fileName.Text = name;
            }
            catch (Exception exp)
            {
                m_Log.SaveLog("frmMain_NotifyBulkInsertInfo:" + exp);
                txtError.Text = "frmMain_NotifyBulkInsertInfo:" + exp;
            }
        }

        /// <summary>
        /// BulkInsertが終了した区分を通知
        /// </summary>
        /// <param name="divNo"></param>
        private void frmMain_EndBulkInsert(int divNo)
        {
            if (divNo > (int)NET10_DIV.Net10Data)
                return;

            // 変更した色を元に戻す
            m_UI[divNo].title.Foreground = new SolidColorBrush(Colors.LawnGreen);
        }

        /// <summary>
        /// BulkInsert処理でのエラーを取得する。
        /// </summary>
        /// <param name="divNo"></param>
        /// <param name="error"></param>
        /// <param name="command"></param>
        private void frmMain_BulkInsertError(int divNo, string error, string command)
        {
            if (divNo > (int)NET10_DIV.Net10Data)
                return;

            // 対象区分の色を変更し、エラー内容を表示する
            m_UI[divNo].title.Foreground = new SolidColorBrush(Colors.Red);
            txtError.Text = error + "File : " + command;
        }

        /// <summary>
        /// コントロール初期化
        /// </summary>
        public void controlInit()
        {
            // 区分毎のUIを配列に格納し、初期化
            m_UI[(int)NET10_DIV.DTMaster].setRecInfo(lbDivName1, lbTotalFileCount1, lbFileCount1, lbFileName1, pbProgress1);
            m_UI[(int)NET10_DIV.Net10Data].setRecInfo(lbDivName2, lbTotalFileCount2, lbFileCount2, lbFileName2, pbProgress2);

            // その他UIの初期化
            lbTargetDB.Text = "";
            lbTargetFileName.Text = "";
            lbTargetDate.Text = "";
            lbRecFolder.Text = "";

            txtError.Text = "";
        }
    }
}
