# Ton, Aufnahmeumrandung und Galerie – 6. Oktober 2026

Die betroffene Aufnahme vom 6. Oktober um 00:17:54 enthält eine AAC-Spur, deren dekodierte Samples vollständig null sind. Die vorherige Prüfung auf vorhandene Tonspuren hatte hörbaren Inhalt noch nicht geprüft. Die neuen Tests prüfen echte Audiosamples.

## Änderungen

- Der Audiomischer liest WASAPI-Blöcke mit einem kurzen Puffer hinter der Aufnahmezeit. Zuvor las er regelmäßig noch nicht eingetroffene Daten und gab Stille aus. Ein eigener wartender Timer hält die CPU-Last niedrig.
- Audioblöcke können an ihren Grenzen teilweise überlappen. Verbrauchte Daten werden aus dem Puffer entfernt; nach längerer Stille setzt ein neues Signal an seiner tatsächlichen Zeitposition ein. IEEE-Float in WAVEFORMATEXTENSIBLE sowie 8-/24-Bit-PCM werden korrekt gelesen. Die Pegelliste wird als unabhängiger Snapshot veröffentlicht.
- Spezi fragt die Windows-Berechtigung für randlose Aufnahme an und verwendet die native IsBorderRequired-Schnittstelle. Der bisherige Zugriff per Reflection erreichte diese Eigenschaft mit dem verwendeten SDK nicht. Auf diesem Computer meldet Windows `Allowed`; die Sitzung meldet `Capture border required: False`.
- RGB32-Videobilder werden für WPF mit einem vollständig deckenden Alphakanal versehen. Das bisherige Padding-Byte machte Vorschaubilder transparent. Die Galerie speichert Vorschauen mit maximal 320 Pixeln Breite im Hintergrund. Cache-Schlüssel berücksichtigen Dateiversion und Bildformat; beschädigte Cache-Dateien werden erneuert.
- Die Galerie bleibt während Rückblick und Aufnahme verfügbar. Vorschaubildfehler einer Datei unterbrechen das Laden der anderen Dateien nicht.
- Der Player öffnet die Quelle nach dem Laden des Fensters. Pause, Spulen und Lautstärke sind bedienbar. Fehler werden angezeigt und bieten das Öffnen im Windows-Videoplayer an. Beim Schließen werden Player und Timer freigegeben.

## Prüfung

- Echte Systemtonaufnahme über „Lautsprecher (2- Focusrite USB Audio)“ mit zwei leisen Tonabschnitten und einer längeren stillen Pause. Der gespeicherte AAC-Ton wurde zu PCM dekodiert: beide Tonabschnitte haben einen RMS-Pegel von etwa 0,198, die Pause 0. Der Ton kehrt nach der Pause zurück.
- Aufnahme bei 1920 × 1080 / 60 FPS: 60 kodierte Bilder pro Sekunde, 0 verworfene Bilder, parallele Rückblick-Clips, vollständige Sitzung, reine Videoaufnahme und Fehlermeldung bei ungültigem Speicherort. Alle Prüfungen bestanden.
- Tatsächlicher WPF-Player: Video öffnet und läuft weiter, Audiospur vorhanden und ungemutet, Pause hält die Position, Spulen und Lautstärke funktionieren. Das gerenderte Player-Fenster wurde visuell geprüft.
- Tatsächliche Galerie während eines laufenden Rückblicks: Dauer und Vorschaubild werden geladen, Alphakanal deckend, Vorschau maximal 320 Pixel breit. MEDIA_TEST_OK.

Die Tests verändern keine gespeicherten App-Einstellungen. Berichte liegen unter `tests/results`. Frühere Quell- und Programmdateien liegen unter `.backups/media-fix-*`. Bereits als Stille gespeicherte Audiosamples enthalten keinen wiederherstellbaren Originalton.

## Tests wiederholen

```powershell
dotnet run --project tests/Spezi.RecordingTests.csproj -c Release -- --audio-loopback-test
dotnet run --project tests/Spezi.RecordingTests.csproj -c Release -- --60fps
dist/Spezi.exe --media-test "C:\Pfad\zur\Aufnahme.mp4"
```

Der Audiotest spielt einen kurzen leisen Testton ab. Der Medientest öffnet kurz das Player-Fenster und verwendet vorübergehend eine Bildschirmaufnahme für die Galerieprüfung.
