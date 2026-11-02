# Spezi – Optimierung vom 06.10.2026

Die Aufnahme verwendete bereits den NVIDIA-Hardwareencoder und lief im Desktoptest mit 1080p bei 60 FPS ohne verworfene Bilder. Verbesserungsbedarf bestand vor allem bei Galerie-Vorschaubildern, temporären Audiodaten und zusätzlichen GPU-Kopien.

## Änderungen

- Die Galerie lädt Vorschaubilder und Laufzeiten für die sichtbaren Einträge. Recycling gibt Referenzen unsichtbarer Einträge frei; abgebrochene Such- und Ladeaufträge werden verworfen. Die Suche wartet 200 ms nach der letzten Eingabe.
- Vorschaubilder werden als eigenständige, eingefrorene Raster mit maximal 320 Pixeln Breite gespeichert. Der frühere skalierte Bildwrapper hielt zusätzlich das vollständige Ausgangsbild im Speicher. Ein auf 64 Einträge begrenzter Cache und ein gemeinsamer Dekodierauftrag verhindern parallele Lastspitzen. Die Laufzeit wird mit der Vorschau zwischengespeichert. Die bisherigen PNG-Caches bleiben verwendbar.
- Die letzten drei Clips auf der Aufnahmeseite verwenden denselben Cache; Vorschaubilder laden auch während eines aktiven Rückblicks.
- Die Audioverarbeitung verarbeitet 48-kHz-Float-Stereo direkt aus dem eingehenden Puffer und kopiert die benötigten Samples sofort in den eigenen Speicher. Für andere Formate wird der Umrechnungspuffer wiederverwendet. Die Geräteliste wird nur bei Änderungen erneuert. AAC schreibt direkt aus dem vorhandenen Datenbereich in den nativen Medienpuffer.
- Die GPU-Farbumwandlung rendert direkt in die dem Encoder gehörende NV12-Oberfläche. Acht zusätzliche NV12-Oberflächen und eine GPU-Kopie je umgewandeltem Bild entfallen. Bei 1080p entsprechen die entfernten Oberflächen rund 24 MiB roher Bilddaten. Jedes Bild behält weiterhin seine eigene Oberfläche; spätere Bilder überschreiben keine noch benötigten Frames. Eingangsansicht und Software-Ausleseoberfläche werden wiederverwendet.
- Im Leerlauf ohne automatische Spielerkennung entfällt die regelmäßige Prozess-/Monitorabfrage. Bekannte Spiele werden ohne zusätzliche Fenstergeometrie erkannt. Die Encoder-Auswahl in den Einstellungen wird für 30 Sekunden zwischengespeichert; beim Start einer Aufnahme wird die Hardware weiterhin neu geprüft.

## Gemessener Vergleich

Messungen auf diesem Rechner, jeweils Release-Build. Die Galerie verwendete 24 Kopien desselben 1080p-Clips, ein 600 × 620 Pixel großes Fenster und einen frischen Cache. Der erste Vergleichslauf zeigte:

| Messung | Vorher | Optimiert |
|---|---:|---:|
| Zusätzlicher privater Speicher durch die Galerie | 281,4 MiB | 60,2 MiB |
| Gesamter belegter Arbeitsspeicher beim Galerietest | 368,1 MiB | 139,6 MiB |
| CPU-Arbeit bis zum Messpunkt | 7.750 ms | 1.984 ms |
| Temporäre verwaltete Daten im Galerietest | 387,0 MiB | 84,2 MiB |
| Erste fünf sichtbare Vorschauen bereit | 782 ms | 801 ms |
| Geladene Vorschauen nach dem Öffnen | 24 | 5 |
| Audio-Umrechnung, 1.000 Blöcke à 20 ms: temporäre Daten | 15.440.040 Byte | 40 Byte |
| AAC, 500 Eingabeblöcke: temporäre Daten | 2.308.040 Byte | 248.040 Byte |
| 300 GPU-Umwandlungen mit regelmäßiger Auslese | 98,51 ms | 92,23 ms |

Der zusätzliche Galeriespeicher sank damit um etwa 79 %, die CPU-Arbeit in diesem Vergleich um etwa 74 %. Ein abschließender Lauf mit Scroll-Prüfung benötigte 62,3 MiB zusätzlichen privaten Speicher und 2.609 ms CPU-Arbeit. Die Werte schwanken zwischen Läufen. Durch das Laden sichtbarer Einträge wird die Arbeit beim Öffnen reduziert und beim Scrollen nachgeholt; die gesamte Bibliothek wird nicht vorab dekodiert.

Bei 1080p/60 FPS mit 15 Mbit/s lag die über alle logischen CPU-Kerne gemittelte Prozesslast im kurzen Desktop-Aufnahmetest vor der Änderung bei 0,234 % und danach bei 0,322 %. Beide Läufe lieferten 60 FPS ohne verworfene Bilder. Eine Verringerung der gesamten Aufnahme-CPU-Last lässt sich daraus nicht belegen. Die Desktopinhalte und die Größe des gepufferten Videos unterschieden sich; der gesamte Aufnahmespeicher ist deshalb kein isolierter Vorher-/Nachher-Vergleich. Die kleine Zeitdifferenz des GPU-Mikrotests ist ebenfalls keine belastbare Aussage über die gesamte GPU-Auslastung.

Dies sind Desktop- und Komponententests, keine Messung in Overwatch oder Rainbow Six Siege. Einfluss auf die Spiel-FPS hängt weiterhin von GPU, Treiber, Auflösung, Bildrate und Spielsituation ab. Der Rückblick hält komprimiertes Video im RAM: 1–5 Minuten benötigen bei 15 Mbit/s ungefähr 113–563 MB Videodaten plus Ton und Verwaltung, abhängig von der tatsächlich erzeugten Bitrate.

## Prüfung und Berichte

Die Berichte liegen unter `tests/results/performance-20261006`.

- Release-Build ohne Warnungen oder Fehler.
- Reale Aufnahme mit 1080p/60 FPS: 60 kodierte FPS, keine verworfenen Bilder, kontinuierliche Zeitstempel; zwei gleichzeitig gespeicherte Clips, vollständige Aufnahme und eigenständige Aufnahme spielbar, Titel und Spielordner korrekt.
- Rückblick-Rollover, AAC-Zeitstempel, Aufnahme ohne Ton sowie fehlgeschlagener Schreibzugriff geprüft. Ein Fehler meldet keinen erfolgreichen Speichervorgang.
- Tatsächlicher WASAPI-Systemton über AAC/MP4 dekodiert; beide Testtonabschnitte vor und nach einer Pause vorhanden, Pause zeitlich erhalten.
- PCM 8/16/24/32 Bit und Float-Stereo mit 44,1/48/96 kHz geprüft, einschließlich Kanalwerten, Pufferbesitz und Abtastratenumrechnung über Blockgrenzen.
- Zurückgehaltene GPU-Bilder bleiben nach zwölf weiteren Bildern unverändert; Größenwechsel 1080p/720p/360p und Software-Auslese erfolgreich.
- Galerie-Player öffnet Video mit Ton; Pause, Springen und Lautstärke funktionieren. Vorschaubilder sind undurchsichtig und klein. Suche nach Spielordner, leere Trefferliste und erneutes Laden funktionieren während aktivem Rückblick.
- Scrollen lädt neue Vorschauen, gibt unsichtbare Vorschauen frei und verwendet beim Zurückscrollen den begrenzten Cache. Die Aufnahmeseite zeigt letzte Clips mit Vorschauen und Laufzeiten während aktivem Rückblick.

Die vorherige Programmversion und die geänderten Originaldateien werden beim Austausch unter `.backups/performance-*` gesichert. Die neue ausführbare Datei bleibt unter `dist/Spezi.exe`; es entsteht kein weiterer dist-Ordner.
