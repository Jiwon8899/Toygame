# GitHub 분할 푸시 및 Pages 배포 결과

- 실행일: 2026-08-10
- 저장소: `https://github.com/Jiwon8899/Toygame.git`
- Pages: `https://jiwon8899.github.io/Toygame/`

## 1. 최종 판정

**성공**

- 136개 기존 로컬 커밋을 원격 `main`에 분할 푸시했다.
- 분할 완료 시 원격 HEAD와 로컬 HEAD가 `d741229b6274c367b611d2ac9bb6403452634fb7`로 일치했다.
- 필수 프로젝트 및 WebGL 파일이 원격 트리에 모두 존재했다.
- GitHub Pages 공개 URL이 최종적으로 `HTTP 200 OK`를 반환했다.
- 이 보고서 커밋을 마지막으로 추가 푸시한다.

## 2. PART A 확인 결과

| 항목 | 결과 |
|---|---|
| 브랜치 | `main` |
| 원격 | `https://github.com/Jiwon8899/Toygame.git` |
| 실행 전 로컬 HEAD | `d741229b6274c367b611d2ac9bb6403452634fb7` |
| 실행 전 로컬 커밋 수 | 136 |
| 실행 전 원격 HEAD | `5f975cd042c7ca2121e27ae5c5e6e93af2b01413` |
| 실행 전 원격 커밋 수 | 1 |
| Git credential helper | Git Credential Manager |
| 인증 프롬프트 | 발생하지 않음 |
| `http.postBuffer` | `524288000` |

사전 민감정보 점검 결과 실제 키·토큰·인증서가 0건임을 확인한 상태에서 공개 저장소 푸시를 진행했다.

## 3. 구간별 push 기록

모든 구간은 오래된 커밋부터 순서대로 전송했으며 각 완료 직후 원격 HEAD를 확인했다. 강제 push, rebase, amend, filter-branch는 사용하지 않았다.

| 순서 | 구간 마지막 SHA | 결과 | 소요 시간 | 시도 횟수 | 완료 후 원격 커밋 수 |
|---:|---|---|---:|---:|---:|
| 1 | `49bdb7e` | 성공 | 6.1초 | 1 | 11 |
| 2 | `5396f87` | 성공 | 5.2초 | 1 | 21 |
| 3 | `832daf0` | 성공 | 147.6초 | 1 | 31 |
| 4 | `60d1d05` | 성공 | 5.5초 | 1 | 41 |
| 5 | `18a6e47` | 성공 | 6.7초 | 1 | 51 |
| 6 | `7ce1982` | 성공 | 5.4초 | 1 | 61 |
| 7 | `d20a4ff` | 성공 | 5.2초 | 1 | 71 |
| 8 | `74597fb` | 성공 | 10.2초 | 1 | 81 |
| 9 | `45e75af` | 성공 | 9.2초 | 1 | 91 |
| 10 | `2c610cf` | 성공 | 7.2초 | 1 | 101 |
| 11 | `68a6a6b` | 성공 | 5.6초 | 1 | 111 |
| 12 | `af19c36` | 성공 | 60.5초 | 1 | 121 |
| 13 | `a37e393` | 성공 | 52.8초 | 1 | 131 |
| 14 | `d741229` | 성공 | 6.2초 | 1 | 136 |
| 최종 | `main` 추적 설정 | 성공 | 2.4초 | 1 | 136 |

- 총 실행 시간: 337.4초(약 5분 37초)
- 분할 축소 재시도: 0회
- 인증 재시도: 0회

## 4. 오류 및 대응

### Git push

- push 오류: 0건
- pack 크기 초과: 0건
- 연결 끊김: 0건
- 인증 또는 권한 오류: 0건
- non-fast-forward 거절: 0건

### Pages 확인

PowerShell `Invoke-WebRequest`의 HEAD 요청은 실행 환경 네트워크 경로 문제로 네 차례 `NETWORK_ERROR`를 반환했다.

| 횟수 | 확인 시각(KST) | 결과 |
|---:|---|---|
| 1 | 2026-08-10 10:21:35 | `NETWORK_ERROR` |
| 2 | 2026-08-10 10:26:35 | `NETWORK_ERROR` |
| 3 | 2026-08-10 10:31:35 | `NETWORK_ERROR` |
| 4 | 2026-08-10 10:36:35 | `NETWORK_ERROR` |

Pages 실패로 오판하지 않도록 같은 환경에서 `curl.exe -I -L`로 교차 확인했고 정상 응답을 받았다. GitHub Pages 설정은 변경하지 않았다.

## 5. 원격 HEAD 및 커밋 수

- 분할 완료 로컬 HEAD: `d741229b6274c367b611d2ac9bb6403452634fb7`
- 분할 완료 원격 HEAD: `d741229b6274c367b611d2ac9bb6403452634fb7`
- 분할 완료 로컬 커밋 수: 136
- 분할 완료 원격 커밋 수: 136
- 판정: 일치

이 보고서가 커밋되면 로컬과 원격 커밋 수는 각각 137개가 된다.

## 6. 원격 필수 파일 확인

| 경로 | 결과 |
|---|---|
| `docs/index.html` | 존재 |
| `docs/.nojekyll` | 존재 |
| `docs/Build/docs.data.unityweb.part000` | 존재 |
| `docs/Build/docs.data.unityweb.part001` | 존재 |
| `Assets/` | 존재 |
| `ProjectSettings/` | 존재 |
| `Packages/` | 존재 |

- 누락 파일: 0개

## 7. Pages URL 확인

- 확인 시각: 2026-08-10 10:36:46 KST
- URL: `https://jiwon8899.github.io/Toygame/`
- 최종 응답: `HTTP/1.1 200 OK`
- 서버: `GitHub.com`
- Content-Type: `text/html; charset=utf-8`
- Content-Length: 6,290 bytes
- Last-Modified: 2026-08-10 10:12:09 KST
- 판정: 공개 배포 성공

## 8. 커밋하지 않고 보존한 사용자 작업

아래 파일은 지시대로 이번 push 및 보고서 커밋에서 제외했다.

- `Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity`
- `Assets/_Recovery/0 (1).unity`
- `Assets/_Recovery/0 (1).unity.meta`
- `Assets/_Recovery/0 (2).unity`
- `Assets/_Recovery/0 (2).unity.meta`

## 9. 중단 지점 및 재개 명령

실패나 중단 지점이 없다. 보고서 커밋의 마지막 push가 실패하는 경우에만 아래 명령으로 재개할 수 있다.

```powershell
cd C:\Users\tommy\Desktop\ToyGame
git push origin main
```

## 10. 사용자 확인 체크리스트

- [ ] GitHub 저장소가 Public인지 확인
- [ ] GitHub Pages Source가 `main` / `/docs`인지 확인
- [ ] 시크릿 창에서 `https://jiwon8899.github.io/Toygame/` 열기
- [ ] 타이틀 로고와 한글 폰트가 정상인지 확인
- [ ] WebGL 로딩 완료 후 싱글플레이 시작 확인
- [ ] 이어하기와 신규 게임 흐름 확인
- [ ] 브라우저 개발자 콘솔에 치명적 오류가 없는지 확인
- [ ] 다른 PC 또는 모바일 네트워크에서 URL 접근 확인
