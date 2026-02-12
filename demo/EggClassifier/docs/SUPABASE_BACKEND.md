# Supabase 백엔드 연동 가이드

## 개요

EggClassifier는 **Supabase (PostgreSQL)** 를 백엔드로 사용하여 사용자 관리 및 검사 로그를 저장합니다.

### 주요 기능

- **사용자 관리**: 회원가입, 로그인, 얼굴 임베딩 저장
- **검사 로그**: 계란 분류 결과 + 이미지 저장
- **실시간 통계**: 정상/불량 개수, 검사 횟수 조회

---

## Supabase 프로젝트 설정

### 1. 프로젝트 생성

1. [https://supabase.com](https://supabase.com) 접속 → 회원가입/로그인
2. **New Project** 클릭
3. 프로젝트 정보 입력:
   - Name: `egg-classifier` (또는 원하는 이름)
   - Database Password: 강력한 비밀번호 설정
   - Region: `Northeast Asia (Seoul)` 추천
4. **Create new project** 클릭 (약 2분 소요)

### 2. 연결 정보 확인

프로젝트 생성 후:
1. **Project Settings** (좌측 하단 톱니바퀴) → **API**
2. 다음 정보를 복사:
   - **Project URL**: `https://your-project-id.supabase.co`
   - **anon public key**: `eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...`

### 3. appsettings.json 설정

`EggClassifier/appsettings.json` 파일에 연결 정보 입력:

```json
{
  "Supabase": {
    "Url": "https://your-project-id.supabase.co",
    "Key": "your-anon-public-key"
  }
}
```

---

## 데이터베이스 스키마

### 테이블 구조

#### 1. users 테이블 (사용자 관리)

| 컬럼명 | 타입 | 설명 |
|--------|------|------|
| idx | integer (PK) | 자동 증가 기본키 |
| user_id | varchar (UNIQUE) | 사용자 아이디 (로그인용) |
| user_password | text | 비밀번호 해시 (SHA256+Salt, "hash:salt" 형식) |
| user_name | varchar | 사용자 이름 (선택) |
| user_face | float[] (ARRAY) | 얼굴 임베딩 벡터 (128차원) |
| user_role | varchar | 사용자 역할 ("USER" 또는 "ADMIN", 회원가입 시 선택) |

#### 2. egg 테이블 (검사 로그)

| 컬럼명 | 타입 | 설명 |
|--------|------|------|
| idx | integer (PK) | 자동 증가 기본키 |
| user_id | varchar (FK) | 검사를 수행한 사용자 ID |
| egg_class | integer | 계란 클래스 (0: 정상, 1: 크랙, 2: 이물질, 3: 탈색, 4: 외형이상) |
| accuracy | double precision | 분류 정확도 (0~1) |
| inspect_date | timestamp | 검사 날짜/시간 (자동 설정) |
| egg_image | bytea | 계란 이미지 (PNG 바이트 배열) |

### 스키마 생성 SQL

Supabase 대시보드 → **SQL Editor** → 다음 쿼리 실행:

```sql
-- users 테이블 생성
CREATE TABLE public.users (
  idx SERIAL PRIMARY KEY,
  user_id VARCHAR NOT NULL UNIQUE,
  user_password TEXT NOT NULL,
  user_name VARCHAR,
  user_face FLOAT[] NOT NULL,
  user_role VARCHAR DEFAULT 'user'
);

-- egg 테이블 생성
CREATE TABLE public.egg (
  idx SERIAL PRIMARY KEY,
  user_id VARCHAR NOT NULL,
  egg_class INTEGER NOT NULL,
  accuracy DOUBLE PRECISION NOT NULL,
  inspect_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  egg_image BYTEA NOT NULL,
  CONSTRAINT fk_user FOREIGN KEY (user_id) REFERENCES public.users(user_id)
);

-- 인덱스 생성 (성능 최적화)
CREATE INDEX idx_users_user_id ON public.users(user_id);
CREATE INDEX idx_egg_user_id ON public.egg(user_id);
CREATE INDEX idx_egg_inspect_date ON public.egg(inspect_date);
```

### Row Level Security (RLS) 설정 (선택)

보안 강화를 위해 RLS를 활성화할 수 있습니다:

```sql
-- RLS 활성화
ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.egg ENABLE ROW LEVEL SECURITY;

-- 정책: 모든 사용자가 users 테이블 읽기/쓰기 가능 (서비스 키 사용)
CREATE POLICY "Enable all access for service role" ON public.users
  FOR ALL USING (true);

CREATE POLICY "Enable all access for service role" ON public.egg
  FOR ALL USING (true);
```

> **참고**: 현재 C# 클라이언트는 `anon` 키를 사용하므로, RLS를 활성화하면 추가 정책이 필요할 수 있습니다. 간단한 프로젝트에서는 RLS를 비활성화하거나 위 정책을 사용하세요.

---

## C# 서비스 구조

### 아키텍처

```
IUserService (인터페이스)
    ├── UserService.cs (레거시 - JSON 파일 방식, 사용 안 함)
    └── SupabaseUserService.cs (현재 사용 중 - Supabase DB)

IInspectionService (인터페이스)
    └── InspectionService.cs (Supabase DB)

SupabaseService.cs (Singleton)
    └── Supabase.Client 관리 (초기화, 연결)
```

### DI 등록 (App.xaml.cs)

```csharp
// Supabase Services
services.AddSingleton<SupabaseService>();
services.AddSingleton<IUserService, SupabaseUserService>();
services.AddSingleton<IInspectionService, InspectionService>();
```

---

## 주요 클래스 설명

### 1. SupabaseService

**역할**: Supabase 클라이언트 초기화 및 관리 (Singleton)

**주요 메서드**:
- `GetClientAsync()` → `Supabase.Client` 반환

**코드 위치**: `Services/SupabaseService.cs`

**동작 흐름**:
1. `appsettings.json`에서 URL과 Key 로드
2. `Supabase.Client` 초기화 (최초 1회)
3. 이후 호출 시 캐시된 클라이언트 반환

### 2. SupabaseUserService

**역할**: IUserService 구현 (사용자 관리)

**주요 메서드**:
- `UserExists(username)` → 사용자 존재 여부
- `RegisterUser(username, password, faceImagePath)` → 회원가입
  - 얼굴 이미지 → 임베딩 추출 (IFaceService)
  - 비밀번호 해싱 (SHA256+Salt)
  - DB에 저장 (`user_face` 배열)
- `ValidateCredentials(username, password)` → 로그인
  - DB 조회 → 비밀번호 검증
  - UserData 반환 (FaceEmbedding 포함)

**코드 위치**: `Services/SupabaseUserService.cs`

**DB 매핑**:
```csharp
UserEntity (C# 모델) ↔ users 테이블 (PostgreSQL)
    ├── Idx ↔ idx
    ├── UserId ↔ user_id
    ├── UserPassword ↔ user_password (형식: "hash:salt")
    ├── UserName ↔ user_name
    ├── UserFace ↔ user_face (float[])
    └── UserRole ↔ user_role
```

### 3. InspectionService

**역할**: IInspectionService 구현 (검사 로그 저장)

**주요 메서드**:
- `SaveInspectionAsync(userId, eggClass, accuracy, eggImage)` → 검사 결과 저장
  - Mat 이미지 → byte[] 변환
  - egg 테이블에 INSERT
- `GetInspectionCountAsync(userId)` → 사용자별 검사 횟수
- `GetInspectionStatsAsync(userId)` → 정상/불량 통계 (normal, defect)

**코드 위치**: `Services/InspectionService.cs`

**DB 매핑**:
```csharp
EggEntity (C# 모델) ↔ egg 테이블 (PostgreSQL)
    ├── Idx ↔ idx
    ├── UserId ↔ user_id
    ├── EggClass ↔ egg_class
    ├── Accuracy ↔ accuracy
    ├── InspectDate ↔ inspect_date
    └── EggImage ↔ egg_image (byte[])
```

---

## 데이터 흐름

### 회원가입 (SignUpViewModel)

```
[사용자 입력]
  → 아이디, 비밀번호, 역할(USER/ADMIN), 얼굴 이미지
        ↓
[얼굴 이미지 → 임베딩 추출]
  → IFaceService.GetFaceEmbedding()
  → float[128] 벡터
        ↓
[비밀번호 해싱]
  → SHA256(password + salt)
  → "hash:salt" 문자열
        ↓
[DB 저장 (Task.Run으로 백그라운드 처리)]
  → SupabaseUserService.RegisterUser(username, password, faceImagePath, role)
  → Supabase.From<UserEntity>().Insert()
        ↓
[users 테이블]
  ✓ user_id, user_password, user_face, user_role 저장 완료
```

### 로그인 (LoginViewModel)

```
[1단계: 자격증명 (Task.Run으로 백그라운드 처리)]
  → 아이디 + 비밀번호 입력
        ↓
[DB 조회]
  → SupabaseUserService.ValidateCredentials()
  → Supabase.From<UserEntity>().Where(x => x.UserId == username).Single()
        ↓
[비밀번호 검증]
  → 입력 비밀번호 + 저장된 salt → SHA256 해싱
  → 저장된 hash와 비교
        ↓
[2단계: 얼굴 인증]
  → DB에서 user_face (float[]) 로드
  → 웹캠에서 실시간 얼굴 탐지 + 임베딩 추출
  → 코사인 유사도 비교 (>= 80%, 연속 10프레임)
        ↓
[로그인 성공]
  ✓ MainViewModel.OnLoginSuccess()
```

### 검사 로그 저장 (DetectionViewModel)

```
[계란 분류 완료]
  → eggClass (0~4), accuracy (0~1), frame (Mat)
        ↓
[이미지 변환]
  → Mat.ToBytes(".png") → byte[]
        ↓
[DB 저장]
  → InspectionService.SaveInspectionAsync()
  → Supabase.From<EggEntity>().Insert()
        ↓
[egg 테이블]
  ✓ user_id, egg_class, accuracy, egg_image 저장 완료
```

---

## 비밀번호 보안

### 해싱 알고리즘

- **알고리즘**: SHA256 + Salt
- **저장 형식**: `"hash:salt"` (콜론으로 구분)
- **Salt**: 16바이트 랜덤 생성 (Base64 인코딩)

### 코드 예시 (SupabaseUserService.cs)

```csharp
// Salt 생성
private static string GenerateSalt()
{
    var bytes = new byte[16];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(bytes);
    return Convert.ToBase64String(bytes);
}

// 비밀번호 해싱
private static string HashPassword(string password, string salt)
{
    using var sha256 = SHA256.Create();
    var combined = Encoding.UTF8.GetBytes(password + salt);
    var hash = sha256.ComputeHash(combined);
    return Convert.ToBase64String(hash);
}

// DB 저장 시
var hash = HashPassword(password, salt);
user.UserPassword = $"{hash}:{salt}";  // "hash:salt" 형식

// DB 조회 시
var parts = user.UserPassword.Split(':');
var storedHash = parts[0];
var storedSalt = parts[1];
var inputHash = HashPassword(inputPassword, storedSalt);
bool isValid = (inputHash == storedHash);
```

---

## 얼굴 임베딩 저장

### 임베딩 생성

1. **얼굴 이미지 입력**: OpenCvSharp Mat (RGB)
2. **전처리**: 112x112 리사이즈, 정규화
3. **MobileFaceNet ONNX 추론**: 128차원 벡터 출력
4. **DB 저장**: PostgreSQL `FLOAT[]` 타입

### 코드 흐름 (회원가입)

```csharp
// 1. 얼굴 이미지 로드
using var faceImage = Cv2.ImRead(faceImagePath);

// 2. 임베딩 추출
var faceEmbedding = _faceService.GetFaceEmbedding(faceImage);
// → float[128]

// 3. DB 저장
var user = new UserEntity {
    UserId = username,
    UserPassword = $"{hash}:{salt}",
    UserFace = faceEmbedding  // float[] → PostgreSQL FLOAT[]
};
await client.From<UserEntity>().Insert(user);
```

### 코드 흐름 (로그인)

```csharp
// 1. DB에서 사용자 조회
var user = await client.From<UserEntity>()
    .Where(x => x.UserId == username)
    .Single();

// 2. 얼굴 임베딩 로드
var savedEmbedding = user.UserFace;  // float[128]

// 3. 웹캠에서 실시간 비교
var currentEmbedding = _faceService.GetFaceEmbedding(webcamFrame);
var similarity = _faceService.CompareFaces(currentEmbedding, savedEmbedding);

// 4. 유사도 검증
if (similarity >= 0.8f) {
    // 로그인 성공
}
```

---

## 검사 로그 활용

### 통계 조회 예시

```csharp
// 사용자별 검사 횟수
var count = await _inspectionService.GetInspectionCountAsync(userId);
Console.WriteLine($"총 검사 횟수: {count}");

// 정상/불량 통계
var (normal, defect) = await _inspectionService.GetInspectionStatsAsync(userId);
Console.WriteLine($"정상: {normal}개, 불량: {defect}개");
```

### DashboardViewModel 연동 (예정)

```csharp
public DashboardViewModel(IInspectionService inspectionService, MainViewModel mainViewModel)
{
    _inspectionService = inspectionService;
    _mainViewModel = mainViewModel;
}

public override async void OnNavigatedTo()
{
    var userId = _mainViewModel.CurrentUsername;
    var stats = await _inspectionService.GetInspectionStatsAsync(userId);

    NormalCount = stats.normal;
    DefectCount = stats.defect;
    TotalInspections = NormalCount + DefectCount;
}
```

---

## 데이터베이스 조회 예시 (SQL)

### 전체 사용자 조회

```sql
SELECT user_id, user_name, user_role
FROM public.users;
```

### 특정 사용자의 검사 로그

```sql
SELECT egg_class, accuracy, inspect_date
FROM public.egg
WHERE user_id = 'your_username'
ORDER BY inspect_date DESC
LIMIT 10;
```

### 클래스별 검사 통계

```sql
SELECT
    egg_class,
    COUNT(*) AS count,
    AVG(accuracy) AS avg_accuracy
FROM public.egg
WHERE user_id = 'your_username'
GROUP BY egg_class;
```

### 불량 계란 비율

```sql
SELECT
    SUM(CASE WHEN egg_class = 0 THEN 1 ELSE 0 END) AS normal_count,
    SUM(CASE WHEN egg_class > 0 THEN 1 ELSE 0 END) AS defect_count,
    ROUND(
        SUM(CASE WHEN egg_class > 0 THEN 1 ELSE 0 END)::NUMERIC / COUNT(*) * 100,
        2
    ) AS defect_rate
FROM public.egg
WHERE user_id = 'your_username';
```

---

## 마이그레이션 (JSON → Supabase)

기존 JSON 파일 기반 사용자를 Supabase로 마이그레이션하려면:

### 1. 레거시 사용자 데이터 조회

```csharp
// UserService.cs 주석 해제 후
var legacyService = new UserService();
var users = legacyService._store.Users;
```

### 2. Supabase로 이전

```csharp
var supabaseService = new SupabaseUserService(/* DI 주입 */);

foreach (var user in users)
{
    // 얼굴 이미지에서 임베딩 추출
    using var faceImage = Cv2.ImRead(user.FaceImagePath);
    var embedding = _faceService.GetFaceEmbedding(faceImage);

    // Supabase에 저장
    var entity = new UserEntity {
        UserId = user.Username,
        UserPassword = $"{user.PasswordHash}:{user.PasswordSalt}",
        UserFace = embedding
    };

    await client.From<UserEntity>().Insert(entity);
}
```

### 3. DI 등록 변경

```csharp
// App.xaml.cs
// services.AddSingleton<IUserService, UserService>();  // 주석 처리
services.AddSingleton<IUserService, SupabaseUserService>();  // 활성화
```

---

## 트러블슈팅

### 1. "Supabase URL 또는 Key가 설정되지 않았습니다"

**원인**: `appsettings.json` 파일이 빌드 출력에 복사되지 않음

**해결**:
1. `EggClassifier.csproj` 확인:
   ```xml
   <ItemGroup>
     <None Update="appsettings.json">
       <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
     </None>
   </ItemGroup>
   ```
2. 빌드 후 `bin/Debug/net8.0-windows/appsettings.json` 파일 존재 확인

### 2. "Foreign key constraint violation"

**원인**: egg 테이블에 존재하지 않는 user_id 참조

**해결**:
- 회원가입 후 로그인하여 user_id가 users 테이블에 존재하는지 확인
- 또는 외래키 제약 조건 제거 (비권장):
  ```sql
  ALTER TABLE public.egg DROP CONSTRAINT fk_user;
  ```

### 3. "Array conversion error"

**원인**: C# `float[]` ↔ PostgreSQL `FLOAT[]` 변환 오류

**해결**:
- `UserEntity.UserFace`가 `float[]` 타입인지 확인
- Postgrest 라이브러리가 자동으로 변환하므로 별도 처리 불필요

### 4. 로그인 시 얼굴 임베딩이 null

**원인**: DB에 임베딩이 저장되지 않았거나 조회 실패

**해결**:
1. Supabase 대시보드 → Table Editor → users 테이블 확인
2. `user_face` 컬럼이 비어있으면 회원가입 재시도
3. 디버깅:
   ```csharp
   var user = await client.From<UserEntity>().Single();
   Console.WriteLine($"Face embedding length: {user.UserFace?.Length}");
   ```

---

## 보안 권장사항

### 1. Service Key 사용 (프로덕션)

현재는 `anon` 키를 사용하지만, 프로덕션에서는 서버에서 `service_role` 키 사용을 권장:

```csharp
var options = new SupabaseOptions {
    AutoRefreshToken = true,
    AutoConnectRealtime = false,
    Headers = new Dictionary<string, string> {
        { "apikey", "service_role_key" }
    }
};
```

### 2. RLS (Row Level Security) 활성화

사용자가 자신의 데이터만 접근하도록 제한:

```sql
-- users 테이블: 본인 데이터만 조회 가능
CREATE POLICY "Users can view own data" ON public.users
  FOR SELECT USING (auth.uid()::text = user_id);

-- egg 테이블: 본인 검사 로그만 조회 가능
CREATE POLICY "Users can view own logs" ON public.egg
  FOR SELECT USING (auth.uid()::text = user_id);
```

### 3. appsettings.json 암호화

민감한 정보는 환경 변수 또는 Azure Key Vault 사용 권장:

```csharp
var url = Environment.GetEnvironmentVariable("SUPABASE_URL");
var key = Environment.GetEnvironmentVariable("SUPABASE_KEY");
```

---

## 성능 최적화

### 1. 인덱스 활용

검색이 빈번한 컬럼에 인덱스 생성:

```sql
CREATE INDEX idx_users_user_id ON public.users(user_id);
CREATE INDEX idx_egg_user_id ON public.egg(user_id);
CREATE INDEX idx_egg_inspect_date ON public.egg(inspect_date);
```

### 2. 이미지 압축

egg_image 크기가 크면 Storage 사용 고려:

```csharp
// 이미지를 Supabase Storage에 업로드
var fileName = $"{userId}_{DateTime.Now:yyyyMMddHHmmss}.png";
await client.Storage.From("egg-images").Upload(imageBytes, fileName);

// DB에는 URL만 저장
entity.EggImageUrl = fileName;
```

### 3. 페이지네이션

대량 데이터 조회 시:

```csharp
var response = await client.From<EggEntity>()
    .Where(x => x.UserId == userId)
    .Order("inspect_date", Ordering.Descending)
    .Range(0, 49)  // 50개씩 페이징
    .Get();
```

---

## 참고 자료

- [Supabase 공식 문서](https://supabase.com/docs)
- [supabase-csharp GitHub](https://github.com/supabase-community/supabase-csharp)
- [PostgreSQL ARRAY 타입](https://www.postgresql.org/docs/current/arrays.html)
- [Row Level Security (RLS)](https://supabase.com/docs/guides/auth/row-level-security)
