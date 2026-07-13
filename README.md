# Chamele-ON Prototype

Unity URP로 제작 중인 **Chamele-ON** iOS 프로토타입입니다. 고품질 Garden 환경에서 캐릭터 이동, 3D 바디 페인팅, 화면 색상 추출, 포즈 변경, 벽 부착을 테스트할 수 있습니다.

## 필요한 버전

- Unity `6000.3.2f1` (Unity 6.3 LTS, Apple Silicon)
- iOS 빌드 시 Unity Hub의 **iOS Build Support** 모듈
- iOS 아카이브/TestFlight 업로드 시 macOS와 Xcode
- Git LFS

## 다른 컴퓨터에서 시작

```bash
git lfs install
git clone git@github.com:TestuyaSaito/Chamele-On.git
cd Chamele-On
git lfs pull
```

Unity Hub에서 **Add project from disk**를 선택한 뒤 이 저장소 폴더를 지정합니다. 처음 열 때 `Library`를 새로 만들기 때문에 에셋 임포트에 시간이 걸릴 수 있습니다.

플레이 장면:

`Assets/ChameleON/Scenes/GardenPrototype.unity`

## 에디터 조작

- 이동: `WASD`
- 카메라: 탐색 모드에서 마우스 오른쪽 드래그
- 페인트: `PAINT`를 누른 뒤 캐릭터에 마우스 왼쪽 드래그
- 페인트 카메라: 빈 공간에서 마우스 왼쪽 드래그
- 색상 추출: `3D PICK`
- 포즈: `POSE`
- 벽 부착: 벽 근처에서 `STICK`

## iOS 빌드

Unity 상단 메뉴에서 `ChameleON URP > Build iOS Xcode Project`를 실행합니다. 생성되는 `Builds/iOS-URP`는 Git에 포함되지 않습니다.

현재 앱 설정:

- Bundle ID: `com.chameleon.prototype`
- Apple Team ID: `PY57847KYX`
- App Store Connect 앱: `Chamele-ON Prototype`

Apple 인증서, `.p12`, 프로비저닝 프로파일, App Store Connect API 키는 보안상 이 저장소에 올리지 않습니다. 새 Mac에서는 Xcode에 같은 Apple ID로 로그인해 자동 서명을 다시 설정하세요.

Windows에서도 Unity 편집과 플레이 테스트는 가능하지만, iOS 빌드와 TestFlight 업로드는 macOS/Xcode에서 진행해야 합니다.

## 버전 관리 범위

Git에는 `Assets`, `Packages`, `ProjectSettings`와 프로젝트 문서만 저장합니다. `Library`, `Builds`, `Logs`, `UserSettings`는 각 컴퓨터에서 다시 생성됩니다. 대용량 텍스처와 3D 모델은 Git LFS로 관리합니다.
