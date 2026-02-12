using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EggClassifier.Core;
using Npgsql;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq; // 데이터 비교를 위해 추가
using System.Windows; // Dispatcher 사용을 위해 추가
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EggClassifier.Features.Dashboard
{
    public partial class DashboardViewModel : ViewModelBase
    {
        private readonly string _connString = "Host=aws-1-ap-northeast-2.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.vvsdcdlazsqkwmaevmvq;Password=dlatkdqls1264";

        // dashboard 화면갱신을 위함
        private DispatcherTimer _refreshTimer;

        [ObservableProperty]
        private int _totalInspections = 0;

        [ObservableProperty]
        private int _normalCount = 0;

        [ObservableProperty]
        private int _defectCount = 0;

        [ObservableProperty]
        private string _logMessage = "";

        // 최근 로그 리스트
        [ObservableProperty]
        private ObservableCollection<EggLogItem> _inspectionLogs = new();

        // 리스트에서 선택한 항목
        [ObservableProperty]
        private EggLogItem _selectedLog;

        // 표시할 이미지소스
        [ObservableProperty]
        private ImageSource _selectedImageSource;

        // 현재 선택된 필터 (0: 정상, 1: 파란, 2: 혈반, 3: 실금, 4: 오염, -1: 전체)
        [ObservableProperty]
        private int _selectedClassFilter = -1; // 기본값 전체


        [RelayCommand]
        private void SetFilter(string classId)
        {
            if (int.TryParse(classId, out int id))
            {
                SelectedClassFilter = id; // 이 값이 바뀌면 OnSelectedClassFilterChanged가 실행됨
            }
        }


        private void SetupTimer()
        {
            // 타이머 중복 생성 방지
            if (_refreshTimer != null) return;

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(1); // 1초마다 갱신
            _refreshTimer.Tick += (s, e) => LoadStatistics();
            _refreshTimer.Start();
        }

        public DashboardViewModel()
        {
            LoadStatistics();
            SetupTimer(); // 생성자에서 타이머 시작
        }

        // 필터 값이 변할때마다 자동으로 호출하여 갱신
        partial void OnSelectedClassFilterChanged(int value)
        {
            // 필터 변경 시 리스트를 비우고 새로 불러와야 하므로 Clear 호출
            _inspectionLogs.Clear();
            LoadStatistics();
        }

        // SelectedLog가 변경될 때(리스트 클릭 시) 자동으로 호출되는 부분입니다.
        partial void OnSelectedLogChanged(EggLogItem value)
        {
            if (value?.EggImage != null)
            {
                SelectedImageSource = ByteArrayToImage(value.EggImage);
            }
            else
            {
                SelectedImageSource = null;
            }
        }

        public void LoadStatistics()
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connString))
                {
                    conn.Open();

                    // 0은 정상, 1~4는 불량으로 합산하는 쿼리
                    string sql = @"
                        SELECT 
                            COUNT(*) as total,
                            COUNT(*) FILTER (WHERE egg_class = 0) as normal,
                            COUNT(*) FILTER (WHERE egg_class IN (1, 2, 3, 4)) as defective
                        FROM egg";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            // ObservableProperty로 생성된 속성들에 값 할당
                            TotalInspections = Convert.ToInt32(reader["total"]);
                            NormalCount = Convert.ToInt32(reader["normal"]);
                            DefectCount = Convert.ToInt32(reader["defective"]);
                        }
                    }

                    // 최신 로그 데이터를 임시로 담을 리스트
                    var latestData = new System.Collections.Generic.List<EggLogItem>();
                    string logSql = @"
                        SELECT idx, user_id, egg_class, accuracy, inspect_date, egg_image
                        FROM egg 
                        WHERE (@filter = -1 OR egg_class = @filter)
                        ORDER BY inspect_date DESC 
                        LIMIT 40";

                    using (var cmd = new NpgsqlCommand(logSql, conn))
                    {
                        cmd.Parameters.AddWithValue("filter", _selectedClassFilter);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                latestData.Add(new EggLogItem
                                {
                                    Idx = Convert.ToInt32(reader["idx"]),
                                    UserId = reader["user_id"].ToString(),
                                    EggClass = Convert.ToInt32(reader["egg_class"]),
                                    Accuracy = Convert.ToDouble(reader["accuracy"]),
                                    InspectDate = Convert.ToDateTime(reader["inspect_date"]),
                                    EggImage = (byte[])reader["egg_image"] // 이미지 바이너리 저장
                                });
                            }
                        }
                    }

                    // UI 스레드에서 리스트의 변경된 부분만 업데이트하여 선택 상태 유지
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 리스트가 비어있으면 전체 추가
                        if (_inspectionLogs.Count == 0)
                        {
                            foreach (var item in latestData) _inspectionLogs.Add(item);
                        }
                        else
                        {
                            // DB의 최신 데이터 중 현재 리스트에 없는 데이터만 맨 앞에 삽입
                            foreach (var newItem in latestData.AsEnumerable().Reverse())
                            {
                                if (!_inspectionLogs.Any(x => x.Idx == newItem.Idx))
                                {
                                    _inspectionLogs.Insert(0, newItem);
                                }
                            }

                            // 40개 초과 데이터 삭제
                            while (_inspectionLogs.Count > 40)
                            {
                                _inspectionLogs.RemoveAt(_inspectionLogs.Count - 1);
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogMessage = $"데이터 로드 실패: {ex.Message}";
            }
        }

        // 이미지 변환 메서드
        private ImageSource? ByteArrayToImage(byte[] imageData)
        {
            try
            {
                if (imageData == null || imageData.Length == 0) return null;

                using var ms = new MemoryStream(imageData);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();

                // 스레드 안전성을 위해 Freeze 호출
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                // 이미지가 안 나오는 구체적인 이유를 확인
                LogMessage = $"이미지 변환 실패: {ex.Message}";
                return null;
            }
        }
    }
}