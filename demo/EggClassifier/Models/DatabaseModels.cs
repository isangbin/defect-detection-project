using System;
using System.Collections.Generic;

namespace EggClassifier.Models
{
    /// <summary>
    /// 계란 판별 데이터 모델 (egg 테이블)
    /// </summary>
    public class EggRecord
    {
        /// <summary>
        /// 판별 이력 고유 번호 (Primary Key, Auto-increment)
        /// </summary>
        public int Idx { get; set; }

        /// <summary>
        /// 판별을 수행한 유저의 ID (FK to users.user_id)
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// 판별 결과 (0:정상, 1:크랙, 2:이물질, 3:탈색, 4:외형이상)
        /// </summary>
        public int EggClass { get; set; }

        /// <summary>
        /// AI 모델이 판단한 신뢰도 (0.0~1.0) - Double Precision
        /// </summary>
        public double Accuracy { get; set; }

        /// <summary>
        /// 판별이 이루어진 날짜와 시간 (Nullable)
        /// </summary>
        public DateTime? InspectDate { get; set; }

        /// <summary>
        /// 실시간 공유를 위해 저장하는 계란 사진 데이터 (바이너리)
        /// </summary>
        public byte[]? EggImage { get; set; }

        /// <summary>
        /// 외부 저장된 이미지의 URL 경로 (Nullable)
        /// </summary>
        public string? EggImageUrl { get; set; }

        /// <summary>
        /// 클래스 한글명 반환 (읽기 전용 프로퍼티)
        /// </summary>
        public string ClassName => EggClass switch
        {
            0 => "정상",
            1 => "크랙",
            2 => "이물질",
            3 => "탈색",
            4 => "외형이상",
            _ => "알 수 없음"
        };
    }

    /// <summary>
    /// 계란 판별 통계 데이터 모델
    /// </summary>
    public class EggStatistics
    {
        /// <summary>
        /// 총 검사 건수
        /// </summary>
        public int TotalInspections { get; set; }

        /// <summary>
        /// 정상 계란 수 (egg_class = 0)
        /// </summary>
        public int NormalCount { get; set; }

        /// <summary>
        /// 결함 계란 수 (egg_class = 1, 2, 3, 4 합계)
        /// </summary>
        public int DefectCount { get; set; }

        /// <summary>
        /// 클래스별 세부 건수 (Key: 클래스명, Value: 건수)
        /// </summary>
        public Dictionary<string, int> ClassBreakdown { get; set; } = new();

        /// <summary>
        /// 통계 시작일
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 통계 종료일
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 평균 정확도 (accuracy 평균)
        /// </summary>
        public double AverageAccuracy { get; set; }
    }
}
