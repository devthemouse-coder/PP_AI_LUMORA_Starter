# 25. 백업 및 복원 안내

## 1. 사전 설정

API/EF 도구가 같은 데이터 및 키 경로를 사용하도록 설정합니다. 아래 예시는 PowerShell 기준이며 각 디렉터리는 운영상 분리합니다.

```powershell
$env:LUMORA_DATA_DIRECTORY = Join-Path $env:LOCALAPPDATA 'P.P. AI - LUMORA\data'
$env:LUMORA_KEY_DIRECTORY = Join-Path $env:LOCALAPPDATA 'P.P. AI - LUMORA\keys'
$env:LUMORA_BACKUP_DIRECTORY = 'E:\Lumora-Backups'
```

DB와 키는 이미 생성된 경우 `--initialize-keys`를 다시 실행하지 않습니다. 백업/복구 명령은 현재 DB 스키마가 애플리케이션의 최신 SQLite Migration과 일치해야 합니다.

## 2. 백업 생성

```powershell
$backup = Join-Path $env:LUMORA_BACKUP_DIRECTORY 'lumora-2026-11-08.lumora-backup'
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --backup $backup
```

명령은 SQLite Online Backup API로 WAL을 포함한 일관 스냅샷을 만들고, DB 무결성/FK/스키마/키를 확인한 뒤 암호화 파일을 생성·재검증합니다. 성공 메시지의 파일 크기와 스키마 버전을 확인합니다. 이미 있는 파일은 덮어쓰지 않으므로 매번 다른 파일명을 사용합니다.

성공 시각은 백업 디렉터리의 `backup-status.json`에 UTC로 기록됩니다. 백업/복원 CLI가 실패하면 마지막 실패 작업과 예외 형식 코드만 기록하며, 경로와 예외 메시지는 저장하지 않습니다. 이 JSON을 직접 읽어 상태를 확인할 수 있으며, 상태 조회용 CLI 명령은 아직 없습니다.

## 3. 별도 키 복구 파일 생성

```powershell
$keyRecovery = 'F:\Offline-Lumora-Keys\lumora-keys-2026-11-08.lumora-keys'
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --export-key-recovery $keyRecovery
```

콘솔에서 12자 이상의 복구 비밀번호를 두 번 입력합니다. 입력은 화면에 표시되지 않으며 비밀번호를 명령행/환경변수/설정 파일에 넣지 않습니다. 복구 파일은 백업 및 DB 디렉터리와 다른 곳에 보관하십시오. 복구 비밀번호도 파일과 다른 안전한 수단에 따로 보관합니다.

## 4. 별도 경로에 복원 검증

API 및 DB를 사용하는 다른 프로세스를 중지하고, 복구 대상 Windows 사용자로 키를 준비합니다. 현재 사용자 DPAPI 키가 정상이라면 가져올 필요가 없습니다.

```powershell
$env:LUMORA_DATA_DIRECTORY = 'D:\Lumora-Restore\data'
$env:LUMORA_KEY_DIRECTORY = 'D:\Lumora-Restore\keys'
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --import-key-recovery $keyRecovery
$restoredDb = Join-Path $env:LUMORA_DATA_DIRECTORY 'lumora.db'
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --restore-backup $backup $restoredDb
```

복원 명령은 새 경로만 허용하며 기존 파일/WAL/SHM을 덮어쓰지 않습니다. 암호화 컨테이너 인증, DB 복원, `integrity_check`, `foreign_key_check`, 최신 스키마 버전, 사용자 암호문 복호화 및 검색 키 ID를 확인한 뒤 최종 파일명으로 이동합니다. 실패하면 새 DB는 게시되지 않고 사용 중인 DB는 변경되지 않습니다. 지원하지 않는 스키마 버전은 자동 Migration하지 않습니다.

## 5. 승인된 운영 DB 교체

기존 DB를 교체해야 할 때만 사용합니다. 먼저 앱/API와 DB를 사용하는 모든 프로세스를 종료하고, 백업 파일 및 키 복구 파일을 확인합니다. 아래의 `--approve-replace`는 명시적 승인입니다.

```powershell
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --replace-backup $backup --approve-replace
```

명령은 기존 DB의 암호화된 사전 백업을 `Backup:Directory`에 새 파일로 생성·검증하고, 복원본도 별도 임시 파일에서 완전히 검증합니다. WAL Checkpoint가 바쁘거나 부수 파일이 남으면 교체를 중단합니다. 준비가 완료되면 같은 볼륨에서 원자 파일 교체를 시도합니다. 교체 실패 시 기존 DB 파일은 유지되고 사전 백업은 보존됩니다. 사전 백업 파일명 충돌, 복구 키 불일치, 잘못된 백업은 진행을 중단합니다.

기존 DB가 있는 교체가 완료되면 사전 백업 및 복원 검증 시각을 상태 파일에 기록합니다. 실패 상태에는 `--replace-backup` 작업명과 예외 형식 코드만 남습니다.

## 6. 장애별 대응

| 증상 | 동작 및 대응 |
|---|---|
| 잘못된 복구 비밀번호/손상된 `.lumora-keys` | 인증 실패로 중단합니다. 키 디렉터리를 만들거나 변경하지 않습니다. 원본 복구 파일과 정확한 비밀번호를 확인합니다. |
| 키 디렉터리가 이미 존재하거나 일부 손상 | 기존 파일을 덮어쓰지 않습니다. 기존 경로를 보존하고, 별도 빈 `LUMORA_KEY_DIRECTORY`로 복구한 뒤 복구 여부를 검증합니다. |
| 백업 인증/태그 실패, 파일 잘림 | 복원 대상에 게시하지 않습니다. 다른 검증된 백업을 사용합니다. `.partial`은 완료 백업으로 취급하지 않습니다. |
| DB `integrity_check`/FK 실패 | 복원 게시/운영 DB 교체를 하지 않습니다. 원본·사전 백업을 보존하고 정상 백업으로 다시 시도합니다. |
| 스키마 버전 불일치 | 이 구현은 자동 업그레이드를 수행하지 않습니다. 해당 스키마를 지원하는 버전의 앱에서 별도 검증해야 합니다. |
| AES Key ID 또는 HMAC Key ID 불일치 | 키 복구 파일을 올바르게 가져왔는지 확인합니다. 새 키 생성/초기화로 우회하지 않습니다. |
| WAL Checkpoint 실패 또는 파일 잠금 | 모든 Lumora 및 DB 프로세스를 종료하고 재시도합니다. WAL/SHM을 수동 삭제하지 않습니다. |

복원 과정이 중단되어 `.restore` 임시 DB가 남으면 파일을 운영 DB로 취급하지 마십시오. 앱이 정지된 것을 확인하고 경로/시각을 검토한 뒤 특정 임시 파일만 정리합니다. 임시 파일을 열어 데이터를 출력하거나 외부로 보내지 않습니다.

## 7. 검증 범위와 복구 한계

통합 테스트는 임시 SQLite 파일에 합성 Tenant/User/Memory/StructuredFact, AES-GCM 암호문, HMAC 생일 월 토큰을 저장하고 백업했습니다. 백업 후 원본 DB를 변경한 다음 백업 파일에서 별도 DB를 복원하여 스냅샷 시점의 행만 남았는지, `integrity_check`/`foreign_key_check`, Memory 및 StructuredFact 복호화, HMAC 토큰 저장소 조회가 정상인지 확인합니다. WAL 활성화 및 백업과 동시에 실행되는 쓰기 테스트도 있습니다.

변조·잘림·잘못된 키의 복원 거부, 기존 파일 덮어쓰기 거부, 손상된 백업으로 DB 교체를 시도했을 때 기존 DB 행과 무결성이 유지되는 것을 별도 임시 DB에서 확인했습니다. 다만 서로 다른 Windows 사용자/PC 실기 복구, OS 강제 종료, 운영 파일이 실제로 잠긴 모든 `File.Replace` 상황, 무결성 검사가 실패하는 정교하게 구성된 DB 및 Migration 불일치 파일은 검증하지 않았습니다. 복구 명령은 서버 시작 시 자동으로 수행되지 않으며, 스키마 자동 Migration도 하지 않습니다.

백업/복원 CLI 오류는 민감한 예외 메시지와 경로를 출력하지 않고 예외 형식 코드만 보고합니다. 진단을 위해 상세 메시지를 켜는 별도 옵션은 없습니다. 기존 운영 DB는 본 검증에서 읽기 전용으로만 다뤘으며, 실제 운영 데이터로 백업·복원 리허설을 수행하지 않았습니다.
