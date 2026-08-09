# NHN 게임 공모전 GitHub Pages 배포 사전 점검

- 점검일: 2026-08-10
- Unity: 6000.4.6f1
- 저장소: `https://github.com/Jiwon8899/Toygame.git`
- 예정 Pages URL: `https://jiwon8899.github.io/Toygame/`
- 원격 상태: 빈 저장소(`git ls-remote origin` 결과 없음)
- 원격 push: 사용자 승인 전 미실행

## 1. 선행 버그 수정 확인

- WebGL 이어하기 저장 수정: `d2aafe0 fix(save): persist WebGL continue data`
- GFCRedSpirit WebGL 폰트 보존: `a8333b5 fix(font): preserve GFC font in WebGL`
- 최종 split WebGL 배포 빌드: `21c33c8 build(webgl): publish optimized split build`
- 기존 증거: `PART_G_webgl_singleplayer.png`, `PART_G_webgl_continue_gameplay.png`

두 선행 수정과 그 이후의 최종 WebGL 플레이 검증 이력이 모두 존재하므로 배포 준비를 계속 진행했다.

## 2. WebGL 설정과 산출물

- 출력 폴더: `docs/`
- Compression Format: Gzip (`webGLCompressionFormat: 1`)
- Decompression Fallback: 활성 (`webGLDecompressionFallback: 1`)
- `.nojekyll`: 존재
- `index.html`: 존재
- 데이터 분할 manifest: 2개 part, 총 170,164,100 bytes
- part 결합 SHA-256: manifest와 일치
- `docs/` 총 크기: 188,047,403 bytes (179.34 MiB)

현재 빌드가 요구 조건을 이미 만족하므로 불필요한 재빌드는 하지 않았다.

## 3. 로컬 브라우저 플레이 검증

- 주소: `http://127.0.0.1:18080/`
- 타이틀 화면 로드: Passed
- `게임 시작` → `혼자 시작`: Passed
- 저장 데이터 선택 화면 표시: Passed
- `이어하기` → 실제 게임플레이 및 HUD 진입: Passed
- 브라우저 JavaScript/Unity error: 0
- 경고: WebGL에서 지원되지 않는 URP FSR 후처리 셰이더 경고 1건. 게임 로드와 플레이에는 영향 없음.

## 4. Git 이력과 추적 범위

- 점검 시점 커밋 수: 135
- 최초 커밋: `5f975cd chore: establish Unity project baseline` (2026-08-03)
- 점검 시점 HEAD: `77928bc feat(theft): notify players when customers steal` (2026-08-10)
- 현재 브랜치: `master` (`main` 전환은 push 승인 후 진행)
- 추적 파일: 4,848개, 총 1,515.74 MiB
- `Assets/`, `ProjectSettings/`, `Packages/`: 추적 확인
- 현재 Unity `.meta` 누락: 0
- 추적되지 않은 `.meta`: 사용자 복구 씬 `Assets/_Recovery/0 (1).unity.meta` 1개만 존재

`.gitignore`는 `Library`, `Temp`, `Obj`, `Build`, `Builds`, `Logs`, `UserSettings`, IDE/OS 산출물을 제외하고 `docs/`를 명시적으로 다시 포함한다.

사용자 작업으로 판단한 아래 항목은 이번 커밋에 포함하지 않는다.

- `Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity`
- `Assets/_Recovery/0 (1).unity`
- `Assets/_Recovery/0 (1).unity.meta`

## 5. 민감정보 점검

현재 파일과 전체 135개 커밋 이력에서 다음을 검사했다.

- PlayFab/Photon 식별자와 비밀키
- API key, access token, client secret, private key, password
- GitHub/OpenAI/Anthropic/AWS/Google/Slack 토큰 서명
- 이메일 주소
- `.env`, 인증서, 개인키, keystore 파일

결과:

- 실제 비밀키·토큰·개인 이메일·인증서 파일: 0건
- `ANTHROPIC_API_KEY`: 실제 값이 아니라 런타임 환경변수 이름만 존재
- `ProjectSettings.asset`의 인증서 password 및 Xbox TitleId: 빈 값
- PlayFab/Photon App ID: 현재 및 이력에서 발견되지 않음
- 외부 런타임 API endpoint: `api.anthropic.com` 1종. 키는 저장소에 포함되지 않음.

## 6. GitHub 파일 크기 제한

- 현재 추적 파일 중 100,000,000 bytes 초과: 0개
- 전체 Git 이력 blob 중 100,000,000 bytes 초과: 0개
- 현재/이력 최대 blob: `docs.data.unityweb` split part, 90 MiB
- `docs` 최대 파일: `docs/Build/docs.data.unityweb.part000`, 90 MiB
- 다음 파일: `docs.data.unityweb.part001`, 72.28 MiB

GitHub의 단일 파일 제한 기준으로 push 가능하다.

## 7. 최초 push 용량과 방식

- Git pack: 약 2.48 GiB
- 빈 원격에 한 번에 전체 이력을 올리면 큰 pack으로 실패할 위험이 있다.
- 사용자 승인 후 최초 커밋부터 여러 구간으로 나눠 순차 push한다.
- `git push --force`, rebase, filter-branch, history rewrite는 사용하지 않는다.
- 각 구간마다 원격 HEAD와 커밋 수를 확인한다.

## 8. 사용자 후속 작업

push 완료 후 GitHub에서 다음을 수행한다.

1. `Settings` → `Pages`
2. Source: `Deploy from a branch`
3. Branch: `main`, Folder: `/docs`
4. `Save`
5. 배포 완료 후 `https://jiwon8899.github.io/Toygame/`을 시크릿 창과 다른 기기에서 확인

## 9. 현재 판정

- WebGL 산출물: Passed
- 로컬 브라우저 실제 플레이: Passed
- Git 추적 범위: Passed
- 민감정보: Passed
- 100MB 제한: Passed
- 원격 push: 승인 대기
- GitHub Pages 공개 URL: push 및 사용자 Pages 설정 전이므로 NotValidated
