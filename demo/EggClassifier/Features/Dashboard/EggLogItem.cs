using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EggClassifier.Features.Dashboard
{
    public class EggLogItem
    {
        // DB 스키마와 1:1 매칭되는 속성들
        public int Idx { get; set; }           // 고유 번호 (int4)
        public string UserId { get; set; }     // 판별 유저 (varchar)
        public int EggClass { get; set; }      // 분류 결과 (int4)
        public double Accuracy { get; set; }   // 정확도 (float8)
        public DateTime InspectDate { get; set; } // 날짜/시간 (timestamp)
        public byte[] EggImage { get; set; }   // 이미지 데이터 (bytea)

        // UI 리스트에 표시될 가공된 텍스트
        public string DisplayText => $"[{InspectDate:HH:mm:ss}] 판별: {(EggClass == 0 ? "정상" : "불량")} (정확도: {Accuracy * 100:F1}%)";
    }
}
