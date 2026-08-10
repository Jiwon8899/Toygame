# 2026-08-10 WebGL 수정분 재빌드 및 재배포 결과

## 1. 최종 판정

- **로컬 배포본 검증 성공**
- 사용자 요청에 따라 검증 범위는 타이틀 표시, 싱글플레이 게임 진입, 브라우저 치명적 오류 0건으로 축소했다.
- GitHub Pages 공개 URL 확인은 시간 제한에 따라 **미확인**으로 남긴다.

## 2. PART A — 사전 점검

- Unity: `6000.4.6f1`
- 빌드 대상: WebGL
- 활성 게임 씬: `Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity`
- Unity Console 컴파일 오류: 0건
- WebGL 설정: Gzip / Decompression Fallback 활성 / `PROJECT:ToyGameResponsive`
- 빌드 직전 로컬 HEAD: `e0d0cd6`
- 확인한 주요 수정 커밋:
  - `7131597 fix(day): preserve state across rollover`
  - `639843b fix(input): restore WebGL pointer lock`
  - `37c3bae fix(input): avoid duplicate WebGL lock request`
  - `e0d0cd6 docs(verify): record rollover and input tests`
- 재빌드 커밋: `5e31e15 build(webgl): rebuild with bug fixes`
- 커밋에서 제외한 사용자 작업:
  - `Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity`
  - `Assets/_Recovery/` 전체

## 3. PART B — Release WebGL 빌드

- 결과: 성공
- Unity 보고 크기: 249.63 MB
- 실제 `docs/` 파일 합계: **179.36 MiB**
- 빌드 시간: 792.25초
- 빌드 오류: 0건
- 빌드 경고: 79건
- 최대 단일 파일: **94,371,840 bytes** (`100,000,000 bytes` 미만)
- 100MB 이상 파일: 0개
- `docs/.nojekyll`: 존재
- `docs/index.html`: 존재
- 데이터 분할:
  - `docs.data.unityweb.part000`: 94,371,840 bytes
  - `docs.data.unityweb.part001`: 75,793,515 bytes
  - 합계: 170,165,355 bytes
- manifest SHA-256: `589b361d21ec90a1e91be62c7de6953ec19cef9331d08546d88c7747b70274a6`
- 분할 파일 재결합 SHA-256: manifest와 일치

## 4. PART C — 축소된 로컬 검증

검증 주소: `http://127.0.0.1:18080/` (쿼리 파라미터 없음)

| 항목 | 결과 | 증거 |
|---|---|---|
| 타이틀 화면 표시 | 통과 | `Screenshots/2026-08-10_redeploy_title.jpg` |
| 게임 시작 → 혼자 시작 → 이어하기 → 게임플레이 진입 | 통과 | `Screenshots/2026-08-10_redeploy_gameplay.jpg` |
| 브라우저 콘솔 치명적 오류 | **0건** | error 로그 없음 |

비치명 경고는 기존에 알려진 URP FSR 미지원 셰이더 경고만 2회 기록됐다.

> `Shader 'Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling' is not supported`

사용자의 시간 제한 지시에 따라 원래 요청에 있던 3일 날짜 전환, UI 카메라, 저장/이어하기 60초 관찰은 이번 재배포 관문에서 생략했다.

## 5. PART D — 커밋 및 push

- WebGL 산출물 커밋: `5e31e15 build(webgl): rebuild with bug fixes`
- 이 보고서와 검증 스크린샷은 별도 문서 커밋으로 포함한다.
- `git push origin main`은 사용자 최종 지시에 따라 승인 요청 없이 수행한다.
- 강제 push, rebase, squash, amend는 사용하지 않는다.

## 6. PART E — GitHub Pages 확인

- **미확인**
- 사용자 지시에 따라 배포 대기와 공개 URL 재검증을 생략했다.
- 따라서 공개 URL의 최신 산출물 반영 여부와 캐시 없는 첫 로딩은 이 보고서에서 통과로 판정하지 않는다.

## 7. 발견된 문제와 심각도

- 비치명: WebGL에서 URP FSR 업스케일링 셰이더 미지원 경고가 발생한다. 게임 진입을 막지 않았다.
- 제출 차단 문제: 축소된 검증 범위에서는 발견되지 않았다.

## 8. 사용자가 직접 확인할 항목

- GitHub Pages 배포 완료 후 `https://jiwon8899.github.io/Toygame/` 새로고침
- 공개 URL에서 최신 수정분과 저장/이어하기 장시간 안정성 확인
- 공개 서버 캐시 없는 첫 방문 로딩 확인
