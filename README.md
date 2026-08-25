# 방과후 수강·지원금 통합 관리

전교생 정보부터 수강, 지원 자격, 정산, 부서별 품의까지 연결하기 위한 Windows 로컬 데스크톱 앱입니다.

현재 구현 범위는 **MVP 7 / v1.1.0**입니다.

- 작업공간 생성·선택
- 전교생 학생정보 직접 입력·수정·삭제·Excel 가져오기/양식 받기
- 방과후 이용권·자유수강권 대상자 직접 입력·Excel 가져오기
- 이용권 대상 학년 저장 시 기존 학생 지원유형 자동 동기화
- 학생명단 기반 학년·반·번호·이름 편집형 드롭다운과 입력값 초기화
- 부서 및 항목별 기본 수강료 직접 입력·Excel 가져오기
- 부서별 기본 수강료 합계 표시와 수강중 학생별 항목 금액 일괄 수정
- 수강 데이터 직접 입력·Excel 가져오기
- 실제 수강 데이터가 있는 학생만 표시하는 수강생 명단
- SQLite 로컬 저장, 외래키·중복 제약·인덱스
- 이후 정산을 위한 전체 관계형 스키마와 원본 변경 revision 기록
- 참고 화면을 반영한 짙은 왼쪽 사이드바와 카드·표 중심 UI
- 학년도별 방과후 이용권·자유수강권 기본 지원 한도
- 방과후 이용권 대상 학생의 수강 부서 및 비용 항목별 차감 우선순위
- 학생별 지원 한도 예외 설정과 변경 사유
- 중복 대상자의 지원 재원 적용 우선순위
- 명시적 정산 데이터 생성과 최신상태 경고
- 선행 작업공간 검증 및 동일 학년도 누적 사용액 계산
- 일반 수익자·이용권 초과금·이용권·자유수강권 배분 결과
- 강사료·수용비·교재비·재료비별 수익자·방과후 이용권·자유수강권 결과 화면
- 부서정보 수정 후 사용자가 실행하는 부서금액 다시 불러오기
- 수기로 조정한 실제 적용금액을 보존하는 안전한 기본금액 갱신
- 수강취소 상태·취소일·사유 보존
- 기본금액과 실제 적용금액 분리 및 항목별 금액 변경
- 작업공간별 변경이력 화면
- 학생별 지원 한도·사용액·잔액과 작업공간별 사용내역
- 최신 정산 결과를 이용한 부서별 품의 집계
- 강사료·수용비·교재비·재료비·교재·재료비 통합 품의 선택
- 일반 수익자·이용권 초과금·이용권 지원금·자유수강권 재원 구분
- 대상 학년 정책에 따라 바뀌는 품의 열 제목
- 부서별 합계와 마지막 전체 합계를 포함한 Excel 품의자료 저장
- 품의 계산 서비스와 Excel 출력 양식 분리
- SQLite 일관성 백업과 `.afbackup` 전체 데이터 백업
- 백업 DB 무결성 검사와 복원 전 자동 안전백업
- 다른 PC에서 사용할 수 있는 백업 복원
- JSON 기반 백업 폴더·GitHub 저장소 설정
- GitHub Release 최신 버전 확인과 설치파일 다운로드 진행률
- GitHub가 제공하는 SHA-256 정보가 있을 때 설치파일 무결성 자동 검증
- 업무 DB를 삭제하지 않는 Inno Setup 사용자별 설치파일 구성
- 태그 푸시 시 설치파일을 만드는 GitHub Actions Release 워크플로
- DataGrid 행·열 가상화, 검색 250ms 지연, 변경이력 최근 2,000건 조회

상세 설계는 [docs/01-system-design.md](docs/01-system-design.md)를 참고하세요.

## 개발 환경

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 또는 VS Code

## 실행

```powershell
dotnet restore
dotnet run --project .\src\AfterSchoolManager\AfterSchoolManager.csproj
```

## Windows 설치파일 만들기

1. .NET 8 SDK와 Inno Setup 6을 설치합니다.
2. `build-installer.ps1`을 실행합니다.
3. `artifacts\installer\AfterSchoolIntegratedManager-Setup-v1.1.0.exe`를 배포합니다.

설치파일은 앱만 `%LOCALAPPDATA%\Programs\AfterSchoolIntegratedManager`에 설치합니다. 업무 DB는 별도 경로에 유지되며 제거 프로그램에서도 삭제하지 않습니다.

## GitHub Release 업데이트 설정

저장소에 `v1.1.0`처럼 `v`로 시작하는 태그를 푸시하면 `.github/workflows/release.yml`이 Windows 설치파일을 생성하여 Release에 첨부합니다. 앱은 사용자가 `업데이트 확인`을 눌렀을 때만 고정된 `monmi0817-tech/AFSC-Manager`의 Release 정보를 확인합니다. 화면에는 저장소 주소를 노출하지 않습니다.

## 데이터 저장 위치

기본값은 `%LOCALAPPDATA%\AfterSchoolIntegratedManager\data\afterschool.db`입니다. 프로그램 설치·업데이트 폴더와 분리되어 있어 앱을 업데이트해도 업무 DB가 유지됩니다.

실행 중 오류가 발생하면 `%LOCALAPPDATA%\AfterSchoolIntegratedManager\app.log`에 기록됩니다. 데이터베이스 스키마는 EXE 내부 리소스로 포함되므로 배포 폴더에서 별도의 `schema.sql` 파일을 찾지 않습니다.

## 주의

현재 저장소는 Linux 기반 작업 환경에서 작성되어 Windows WPF 빌드를 직접 실행하지 못했습니다. Windows에서 위 명령으로 복원·빌드 검증을 진행해야 합니다.
