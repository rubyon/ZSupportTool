using AutoUpdaterDotNET;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using WindowsHook;
using WindowsInput;
using ZSupport.Properties;
using Application = System.Windows.Application;
using KeyEventArgs = WindowsHook.KeyEventArgs;
using KeyPressEventArgs = WindowsHook.KeyPressEventArgs;
using Keys = WindowsHook.Keys;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtons = WindowsHook.MouseButtons;
using MouseEventArgs = WindowsHook.MouseEventArgs;
using Point = System.Windows.Point;

namespace ZSupport
{
    public partial class MainWindow : Window
    {
        // 윈도우 이름 검색 (중복 실행 방지용)
        [DllImportAttribute("user32.dll", EntryPoint = "FindWindow")]
        public static extern int FindWindow(string clsName, string wndName);

        // 마우스 커서위치 알아내기 시작
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(ref Win32Point pt);
        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point
        {
            public Int32 X;
            public Int32 Y;
        };
        public static Point GetMousePosition()
        {
            var w32Mouse = new Win32Point();
            GetCursorPos(ref w32Mouse);

            return new Point(w32Mouse.X, w32Mouse.Y);
        }
        // 마우스 커서위치 알아내기 끝

        // 키보드 마우스 입력 막기 (관리자 권한 필요)
        [DllImport("user32.dll")]
        static extern bool BlockInput(bool fBlockIt);

        // 마우스 커서위치 설정하기
        [DllImport("user32.dll")]
        static extern int SetCursorPos(int x, int y);

        private IKeyboardMouseEvents m_Events;

        public static bool isPlaying = false;   // 서포트 생성중인가?
        private bool isFirstClick = true;       // Z키 처음클릭인가?
        private bool isLastClick = false;       // Z키 마지막클릭인가?
        private int oldInterval = 0;            // 직전에 생성한 서포트 갯수 (Undo 용)

        // 직선방정식용
        private double x1 = 0;
        private double x2 = 0;
        private double y1 = 0;
        private double y2 = 0;
        private string tooltipStatus = "";

        // 키보드&마우스 컨트롤용
        private InputSimulator inputSimulator = new InputSimulator();

        // 현재 활성화된 윈도우 이름 알아내기 시작
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        private static string GetActiveWindowTitle()
        {
            const int nChars = 256;
            StringBuilder Buff = new StringBuilder(nChars);
            IntPtr handle = GetForegroundWindow();

            if (GetWindowText(handle, Buff, nChars) > 0)
            {
                return Buff.ToString();
            }
            return null;
        }
        // 현재 활성화된 윈도우 이름 알아내기 끝

        public MainWindow()
        {
            // 중복 실행 방지
            this.Title = "ZSupport Tool";
            if (FindWindow(null, Title) > 1)
            {
                Close();
            }

            InitializeComponent();

            SubscribeGlobal();

            // 자동업데이트 용
            AutoUpdater.Start("http://3dpinside.com/publish/zsupport.xml");

            // 프로그램 시작 위치 X & Y 값 + 10
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = SystemParameters.WorkArea.Width - 210;
            this.Top = SystemParameters.WorkArea.Height - 210;

            // 프로그램 버전 표시
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            labelVersion.Content = String.Format("{0}. {1}.{2}.{3}.{4}", Strings.version, version.Major, version.Minor, version.Build, version.Revision);
        }

        // 직선방정식 함수 시작
        static double getY(double x1, double y1, double x2, double y2, double x)
        {
            return (y2 - y1) / (x2 - x1) * (x - x1) + y1;
        }

        static double getX(double x1, double y1, double x2, double y2, double y)
        {
            return (y - y1) * ((x2 - x1) / (y2 - y1)) + x1;
        }
        // 직선방정식 함수 끝

        // 프로그램 종료시
        private void Window_Exit(object sender, CancelEventArgs e)
        {
            this.Zstart.IsOpen = false;
            Unsubscribe();
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void SubscribeGlobal()
        {
            Unsubscribe();
            Subscribe(Hook.GlobalEvents());
        }

        private void Subscribe(IKeyboardMouseEvents events)
        {
            m_Events = events;
            m_Events.KeyDown += OnKeyDown;
            m_Events.KeyUp += OnKeyUp;
            m_Events.KeyPress += HookManager_KeyPress;
            m_Events.MouseUp += OnMouseUp;
            m_Events.MouseClick += OnMouseClick;
            m_Events.MouseDoubleClick += OnMouseDoubleClick;
            m_Events.MouseMove += HookManager_MouseMove;
            m_Events.MouseDragStarted += OnMouseDragStarted;
            m_Events.MouseDragFinished += OnMouseDragFinished;
            m_Events.MouseDown += OnMouseDown;
        }

        private void Unsubscribe()
        {
            if (m_Events == null) return;
            m_Events.KeyDown -= OnKeyDown;
            m_Events.KeyUp -= OnKeyUp;
            m_Events.KeyPress -= HookManager_KeyPress;
            m_Events.MouseUp -= OnMouseUp;
            m_Events.MouseClick -= OnMouseClick;
            m_Events.MouseDoubleClick -= OnMouseDoubleClick;
            m_Events.MouseMove -= HookManager_MouseMove;
            m_Events.MouseDragStarted -= OnMouseDragStarted;
            m_Events.MouseDragFinished -= OnMouseDragFinished;
            m_Events.MouseDown -= OnMouseDown;
            m_Events.Dispose();
            m_Events = null;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            //Console.WriteLine(string.Format("KeyDown  \t\t {0}\n", e.KeyCode));
            if (GetActiveWindowTitle() != null && GetActiveWindowTitle().Contains("CHITUBOX"))
            {
                //취소 ESC
                if (e.KeyData == Keys.Escape)
                {
                    isFirstClick = true;
                    isLastClick = false;
                    this.Zstart.IsOpen = false;
                }
                //서포터 갯수 감소 X
                if (e.KeyData == Keys.X)
                {
                    interval.Text = (int.Parse(interval.Text) - 1).ToString();
                    if (int.Parse(interval.Text) >= 2)
                    {
                        tbZstart.Text = Regex.Replace(tbZstart.Text.ToString(), @"[0-9]+", int.Parse(interval.Text).ToString());
                    }
                }
                //서포터 갯수 증가 C
                if (e.KeyData == Keys.C)
                {
                    interval.Text = (int.Parse(interval.Text) + 1).ToString();
                    tbZstart.Text = Regex.Replace(tbZstart.Text.ToString(), @"[0-9]+", int.Parse(interval.Text).ToString());
                }
                //서포터삭제 S
                if (e.KeyData == Keys.S)
                {
                    Dispatcher.BeginInvoke(new Action(delegate ()
                    {
                        inputSimulator.Keyboard.KeyDown(WindowsInput.Native.VirtualKeyCode.LMENU);
                        inputSimulator.Mouse.LeftButtonClick();
                        inputSimulator.Keyboard.KeyUp(WindowsInput.Native.VirtualKeyCode.LMENU);
                    }));
                }
                //서포터 생성 취소
                if (e.KeyData == Keys.U)
                {
                    if (isLastClick == true)
                    {
                        BlockInput(true);
                        Dispatcher.BeginInvoke(new Action(delegate ()
                        {
                            for (int i = 0; i < oldInterval; i++)
                            {
                                inputSimulator.Keyboard.ModifiedKeyStroke(WindowsInput.Native.VirtualKeyCode.CONTROL, WindowsInput.Native.VirtualKeyCode.VK_Z);
                                Thread.Sleep(1);
                            }
                        }));
                        BlockInput(false);
                        isLastClick = false;
                        this.Zstart.IsOpen = false;
                    }
                }
                //서포터 갯수 변경후 다시 생성
                if (e.KeyData == Keys.R)
                {
                    if (isLastClick == true)   // 두번쨰 Z 클릭 상태일때 실행
                    {
                        Dispatcher.BeginInvoke(new Action(delegate ()
                        {
                            this.Zstart.IsOpen = false;

                            BlockInput(true);

                            for (int i = 0; i < oldInterval; i++)
                            {
                                inputSimulator.Keyboard.ModifiedKeyStroke(WindowsInput.Native.VirtualKeyCode.CONTROL, WindowsInput.Native.VirtualKeyCode.VK_Z);
                                Thread.Sleep(1);
                            }
                            Thread.Sleep(500);
                            
                            double dX = (x2 - x1);
                            double dY = (y2 - y1);
                            
                            List<Point> xys = new List<Point>();
                            
                            oldInterval = int.Parse(interval.Text.ToString());
                            
                            if (dX != 0)
                            {
                                double stepX = (x2 - x1) / ((double)oldInterval - 1);
                                for (int i = 0; i < oldInterval; i++)
                                {
                                    double x = x1 + stepX * i;
                                    double y = getY(x1, y1, x2, y2, x);
                                    xys.Add(new Point() { X = (int)x, Y = (int)y });
                                }
                            }
                            else if (dY != 0)
                            {
                                double stepY = (y2 - y1) / ((double)oldInterval - 1);
                                for (int i = 0; i < oldInterval; i++)
                                {
                                    double y = y1 + stepY * i;
                                    double x = getX(x1, y1, x2, y2, y);
                                    xys.Add(new Point() { X = (int)x, Y = (int)y });
                                }
                            }

                            Point firstP = xys[0];
                            
                            SetCursorPos((int)firstP.X, (int)firstP.Y);
                            
                            Thread.Sleep(300);
                            
                            for (int i = 0; i < xys.Count; i++)
                            {
                                Point p = xys[i];
                                SetCursorPos((int)p.X, (int)p.Y);
                                Thread.Sleep(150);
                                inputSimulator.Mouse.LeftButtonClick();
                            }
                            
                            BlockInput(false);
                            
                            isPlaying = false;
                            isFirstClick = true;
                            isLastClick = true;
                            tooltipStatus = "option";
                            this.Zstart.IsOpen = true;
                        }));
                    }
                }

                if (isPlaying == false)
                {
                    //Console.WriteLine(string.Format("KeyDown  \t\t {0}\n", e.KeyCode));
                    Point pos = GetMousePosition();

                    if (e.KeyData == Keys.Z && isFirstClick == true)
                    {
                        this.Zstart.IsOpen = true;
                        tooltipStatus = "esc";
                        tbZstart.Text = String.Format("[{0}] {1}", interval.Text.ToString(), Strings.ESC);
                        isFirstClick = false;
                        //Console.WriteLine("Start " + string.Format("x={0:0000}; y={1:0000}\n", pos.X, pos.Y));
                        x1 = pos.X;
                        y1 = pos.Y;
                    }
                    else if (e.KeyData == Keys.Z && isFirstClick == false)
                    {
                        this.Zstart.IsOpen = false;
                        isPlaying = true;
                        //Console.WriteLine("End " + string.Format("x={0:0000}; y={1:0000}\n", pos.X, pos.Y));

                        x2 = pos.X;
                        y2 = pos.Y;

                        double dX = (x2 - x1);
                        double dY = (y2 - y1);

                        List<Point> xys = new List<Point>();

                        oldInterval = int.Parse(interval.Text.ToString());

                        if (dX != 0)
                        {
                            double stepX = (x2 - x1) / ((double)oldInterval - 1);
                            for (int i = 0; i < oldInterval; i++)
                            {
                                double x = x1 + stepX * i;
                                double y = getY(x1, y1, x2, y2, x);
                                xys.Add(new Point() { X = (int)x, Y = (int)y });
                            }
                        }
                        else if (dY != 0)
                        {
                            double stepY = (y2 - y1) / ((double)oldInterval - 1);
                            for (int i = 0; i < oldInterval; i++)
                            {
                                double y = y1 + stepY * i;
                                double x = getX(x1, y1, x2, y2, y);
                                xys.Add(new Point() { X = (int)x, Y = (int)y });
                            }
                        }
                        if (dX == 0 && dY == 0)
                        {
                            isPlaying = false;
                            isFirstClick = true;
                        }
                        else
                        {
                            Dispatcher.BeginInvoke(new Action(delegate ()
                            {
                                BlockInput(true);
                                Point firstP = xys[0];
                                SetCursorPos((int)firstP.X, (int)firstP.Y);
                                Thread.Sleep(300);
                                for (int i = 0; i < xys.Count; i++)
                                {
                                    Point p = xys[i];
                                    SetCursorPos((int)p.X, (int)p.Y);
                                    Thread.Sleep(150);
                                    inputSimulator.Mouse.LeftButtonClick();
                                }
                                BlockInput(false);
                                isPlaying = false;
                                isFirstClick = true;
                                isLastClick = true;
                                tooltipStatus = "option";
                                this.Zstart.IsOpen = true;
                            }));
                        }
                    }
                }
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            //Console.WriteLine(string.Format("KeyUp  \t\t {0}\n", e.KeyCode));
            if (GetActiveWindowTitle() != null && GetActiveWindowTitle().Contains("CHITUBOX"))
            {
                if (e.KeyData == Keys.B)
                {
                    Dispatcher.BeginInvoke(new Action(delegate ()
                    {
                        BlockInput(true);
                        Point pos = GetMousePosition();
                        SetCursorPos((int)pos.X - 100, (int)pos.Y);
                        Thread.Sleep(100);
                        Point pos2 = GetMousePosition();
                        SetCursorPos((int)pos2.X, (int)pos2.Y + 360);
                        Thread.Sleep(100);
                        inputSimulator.Mouse.RightButtonDown();
                        SetCursorPos((int)pos2.X, (int)pos2.Y);
                        inputSimulator.Mouse.RightButtonUp();
                        BlockInput(false);
                    }));
                }
            }
        }

        private void HookManager_KeyPress(object sender, KeyPressEventArgs e)
        {
            //Console.WriteLine(string.Format("KeyPress \t\t {0}\n", e.KeyChar));
        }

        private void HookManager_MouseMove(object sender, MouseEventArgs e)
        {
            //Console.WriteLine(string.Format("x={0:0000}; y={1:0000}", e.X, e.Y));
            Point pos = GetMousePosition();
            double realX = PixelsToPoints((int)pos.X, LengthDirection.Horizontal);
            double realY = PixelsToPoints((int)pos.Y, LengthDirection.Vertical);
            this.Zstart.HorizontalOffset = realX + 12;
            this.Zstart.VerticalOffset = realY + 12;

            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                if (tooltipStatus == "esc")
                {
                    tbZstart.Text = String.Format("[{0}] {1}", interval.Text.ToString(), Strings.ESC);
                }
                else if (tooltipStatus == "option")
                    {
                    tbZstart.Text = String.Format("[{0}] {1}", interval.Text.ToString(), Strings.ZendOption);
                }
            }));
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (GetActiveWindowTitle() != null && GetActiveWindowTitle().Contains("CHITUBOX"))
            {
                if (e.Button == MouseButtons.Right)
                {
                    Dispatcher.BeginInvoke(new Action(delegate ()
                    {
                        inputSimulator.Keyboard.KeyDown(WindowsInput.Native.VirtualKeyCode.LMENU);
                        inputSimulator.Mouse.LeftButtonClick();
                        inputSimulator.Keyboard.KeyUp(WindowsInput.Native.VirtualKeyCode.LMENU);
                    }));
                }
                if (e.Button == MouseButtons.Left)
                {
                    isLastClick = false;
                    Zstart.IsOpen = false;
                }
            }
            else
            {
                isFirstClick = true;
                isLastClick = false;
                Zstart.IsOpen = false;
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            //Console.WriteLine(string.Format("MouseUp \t\t {0}\n", e.Button));
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            //Console.WriteLine(string.Format("MouseClick \t\t {0}\n", e.Button));
        }

        private void OnMouseDoubleClick(object sender, MouseEventArgs e)
        {
            //Console.WriteLine(string.Format("MouseDoubleClick \t\t {0}\n", e.Button));
        }

        private void OnMouseDragStarted(object sender, MouseEventArgs e)
        {
            //Console.WriteLine("MouseDragStarted\n");
        }

        private void OnMouseDragFinished(object sender, MouseEventArgs e)
        {
            //Console.WriteLine("MouseDragFinished\n");
        }

        private void buttonClose_Click(object sender, RoutedEventArgs e)
        {
            Unsubscribe();
            Application.Current.Shutdown();
        }

        private void buttonUpdate_Click(object sender, RoutedEventArgs e)
        {
            AutoUpdater.ReportErrors = true;
            AutoUpdater.Start("http://3dpinside.com/publish/zsupport.xml");
        }

        private void buttonZerone_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("http://zerone3d.3dpinside.com");
        }

        private double PointsToPixels(double wpfPoints, LengthDirection direction)
        {
            if (direction == LengthDirection.Horizontal)
            {
                return wpfPoints * Screen.PrimaryScreen.WorkingArea.Width / SystemParameters.WorkArea.Width;
            }
            else
            {
                return wpfPoints * Screen.PrimaryScreen.WorkingArea.Height / SystemParameters.WorkArea.Height;
            }
        }

        private double PixelsToPoints(int pixels, LengthDirection direction)
        {
            if (direction == LengthDirection.Horizontal)
            {
                return pixels * SystemParameters.WorkArea.Width / Screen.PrimaryScreen.WorkingArea.Width;
            }
            else
            {
                return pixels * SystemParameters.WorkArea.Height / Screen.PrimaryScreen.WorkingArea.Height;
            }
        }

        public enum LengthDirection
        {
            Vertical, // |
            Horizontal // ——
        }

        private void buttonHelp_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(String.Format("https://github.com/rubyon/zsupport/blob/main/{0}.md", Strings.readme));
        }
    }
}
