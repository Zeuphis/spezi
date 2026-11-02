# Page Up / Page Down – 06.10.2026

Page Up (Bild auf, virtueller Tastencode 0x21) und Page Down (Bild ab, 0x22) waren bereits speicherbar. Die Oberfläche verwendete jedoch die internen WPF-Namen `Prior` und `Next`. Sie zeigt jetzt eindeutig `Page Up` und `Page Down`, auch in Kombination mit Strg, Alt oder Umschalt.

Beim Anklicken eines Hotkey-Feldes werden beide bisherigen globalen Hotkeys vorübergehend freigegeben. Dadurch kann auch eine bereits von Spezi belegte Taste neu eingegeben werden, ohne einen Clip oder eine Aufnahme auszulösen. Nach Verlassen des Feldes, Wechsel der Seite oder Wechsel zu einer anderen Anwendung werden die Hotkeys wieder aktiviert. Wiederholte Tastendrücke beim Gedrückthalten speichern nicht ständig erneut.

Vor einer neuen Registrierung werden beide alten Zuordnungen freigegeben. Dadurch lassen sich die Tasten für Clip und Aufnahme tauschen, ohne dass die alte zweite Zuordnung die neue erste blockiert. Eine doppelte Zuordnung bleibt ein Fehler und lässt keine veralteten globalen Hotkeys aktiv.

Auf diesem Rechner war Page Up zusätzlich von einer anderen Anwendung global belegt (Windows-Fehler 1409). Für eine solche belegte Page-Taste verwendet Spezi eine alternative Tastenerkennung. Sie läuft auf einem eigenen, wartenden Nachrichtenthread und wird nur bei einer tatsächlich belegten Page-Taste aktiviert. Sie fragt die Tastatur nicht fortlaufend ab. Nur die gewählte Page-Taste mit den passenden Zusatztasten wird als Spezi-Hotkey verarbeitet; während der Bearbeitung und nach dem Beenden wird diese Erkennung entfernt. Die zugeordnete Taste löst währenddessen Spezi aus und wird nicht zusätzlich an die Anwendung mit der bisherigen Belegung weitergereicht. Andere belegte Tastenkombinationen melden weiterhin einen Konflikt.

Die Implementierung folgt den Hinweisen zur schnellen Weitergabe der Arbeit an einen anderen Thread in der [Microsoft-Dokumentation zu LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc).

Verwendung: Einstellungen → Tastenkombinationen → gewünschtes Feld anklicken → Page Up oder Page Down drücken → außerhalb des Feldes klicken. Zum Speichern eines Clips muss der Rückblick laufen.

Die Prüfung `Spezi.exe --hotkey-test` kontrolliert eindeutige Beschriftungen, Aktivierung beider Tasten ohne Zusatztasten, Freigabe beim Bearbeiten, Windows-Nachrichtenverarbeitung, beide echten Einstellungsfelder, unveränderte Scrollposition, Tauschen und erneute Registrierung, Fensterwechsel sowie Serialisierung. Die alternative Erkennung wird mit absichtlich reservierten Page-Tasten und simulierten Windows-Tastendrücken geprüft; wiederholte Tastendrücke lösen nur einmal aus. Die Testtasten werden durch den isolierten Handler abgefangen. Die Prüfung startet keine Aufnahme und verändert die persönlichen Einstellungen nicht. Der Testbericht wird unter `%TEMP%/SpeziHotkeyTest` abgelegt.

Die vorherige Programmversion und die geänderten Originaldateien werden vor dem Austausch unter `.backups/hotkeys-*` gesichert. Es bleibt ausschließlich der vorhandene `dist`-Ordner bestehen.
