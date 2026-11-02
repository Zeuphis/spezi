# Spezi 1.0.1 – Videos im Windows-Standardplayer

Die Abspielknöpfe in der Galerie und bei den letzten Clips auf der Startseite öffnen das Video jetzt direkt mit der Windows-Standard-App für MP4-Dateien. Je nach Windows-Einstellung ist das beispielsweise Medienwiedergabe oder VLC. Spezi legt selbst keinen bestimmten Player fest.

Der integrierte Player einschließlich seiner Oberfläche, Wiedergabesteuerung und Aktualisierungstimer wurde entfernt. Die Galerie mit Vorschaubildern, Suche, Umbenennen und Löschen bleibt erhalten. Verschobene oder gelöschte Dateien und Fehler beim Öffnen werden verständlich angezeigt.

Die Portable-Version befindet sich wie bisher in `dist`. Der aktualisierte Installer ist `installer/Spezi-Setup.exe`. Den Installer ausführen, um eine bereits installierte Spezi-Version zu aktualisieren; Einstellungen und Clips werden dabei erhalten.

Geprüft:

- Release-Build ohne Fehler und Warnungen.
- Player-Klasse und Player-XAML sind nicht mehr in der kompilierten App enthalten.
- Beide tatsächlichen Abspielknöpfe übergeben den Testclip mit Leerzeichen und Unicode im Dateinamen an Windows, ohne ein Spezi-Playerfenster anzulegen.
- Galerie-Vorschaubilder, Dauerangaben, Ordnersuche und letzte Clips funktionieren während einer aktiven Replay-Aufnahme.
- Der getrennte Oberflächentest verändert keine Benutzereinstellungen.

Die Wiedergabe selbst übernimmt die Windows-Standard-App. Ihre Bild- und Tonwiedergabe wurde bei diesem Update nicht separat bewertet.
