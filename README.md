# Automated Macro

Program za Windows koji snima i izvodi procedure (makroe): klikove mišem, unos teksta, čekanja i tipke, grupirane u mape i ponavljane u petlji.

## Preuzimanje i pokretanje

Gotov program je u **Releases** (desno na GitHub stranici repozitorija): preuzmi `AutomatedMacro-*-win-x64-portable.exe` i pokreni ga dvoklikom.

- **Prijenosan je:** jedna datoteka, bez instalacije. Radi na svakom računalu s Windows 10 ili 11 (64-bit), bez instaliranog .NET-a.
- Postavke (`settings.json`) i procedure (mapa `Makroi`) stvaraju se **pokraj exe datoteke**. Kad premještaš program na drugo računalo, kopiraj cijelu mapu i sve ide s njim. Ako se u tu mapu ne može pisati (npr. `Program Files`), postavke idu u `%AppData%\AutomatedMacro`.
- Pri prvom pokretanju na novom računalu Windows može prikazati „Windows je zaštitio vaše računalo” jer exe nije digitalno potpisan. Klikni **Više informacija → Ipak pokreni**.

## Snimanje koraka

1. Klikni **Snimi (F9)**. Glavni prozor se minimizira, a gore desno se pojavi mali panel **Snimanje**.
2. Klikni u drugom programu, npr. na gumb. Panel to prikaže pod **Zadnji klik**.
3. **F8** (ili **Spremi korak**) sprema taj klik kao korak. Klikovi na sam panel se ne snimaju.
   - Za polje za tekst prije F8 odaberi **Klik + upiši tekst**. Može fiksni tekst ili nasumični jedinstveni tekst od 3 do 40 znakova (skup znakova, prefiks i sufiks su neobavezni).
   - Dvoklik i povlačenje prepoznaju se sami.
4. Iz panela možeš dodati i čekanje, tipku (Enter, Tab, Ctrl+V…) ili **novu grupu**. Sve što snimiš nakon toga ide u tu grupu, a gumb ↑ izlazi iz grupe.
5. **F9** (ili **Završi snimanje**) završava snimanje.

## Uređivanje

- Tablica prikazuje hijerarhiju (1, 2, 2.1…). Grupe se otvaraju i zatvaraju kao mape u Exploreru.
- **Povuci i ispusti** mijenja redoslijed. Ispusti na sredinu grupe da korak premjestiš u nju.
- Alt+↑/↓ pomiče odabrani korak. Ctrl+C / X / V / D kopira, izrezuje, lijepi i duplicira. Space uključuje ili isključuje korak, a Del ga briše.
- Desni panel uređuje odabrani korak: poziciju (gumb **Odaberi** uzima točku izravno s ekrana), tekst, **pauzu nakon koraka** itd. Za grupu se u **Izgled grupe** bira boja ikone.
- **Pregled** (dolje desno) pokazuje gdje na ekranu odabrani korak klika.

## Izvođenje

- **Pokreni (F5)** izvodi cijelu proceduru. Prije starta odbrojava („Odgoda starta”, zadano 3 s) da stigneš prebaciti na ciljni prozor.
- **Postavke petlje:** beskonačno ili zadani broj ponavljanja, uz pauzu između ciklusa. Ako je „Petlja” isključena, izvodi se jednom.
- **F10** zaustavlja izvođenje u bilo kojem trenutku, i dok je prozor minimiziran. Mala obavijest o statusu na vrhu ekrana propušta klikove.
- **Izvedi samo ovo** izvodi samo odabrani korak ili grupu, jednom.

## Klikovi u igrama (Roblox)

Obični klik premjesti kursor „teleportom” (`SetCursorPos`) i drži tipku oko 20 ms. Obični programi to registriraju. Igre koje čitaju raw input (npr. Roblox) pritom ne vide da se miš pomaknuo, pa klik ode na mjesto gdje igra misli da je miš, a pritisak je kraći od jedne sličice igre.

Za takve igre uključi **Postavke petlje → Klik za igre**. Tada program za svaki klik:

1. aktivira prozor igre ako nije u prvom planu (inače prvi klik samo prebaci fokus);
2. dovede miš kao pravi korisnik, kroz `SendInput` s nekoliko relativnih koraka, pa igra vidi pomak;
3. pričeka na gumbu i drži klik zadano vrijeme (zadano 80 ms). Na slabijem računalu ili s više klijenata povećaj na 120–200 ms.

Postavka se sprema uz proceduru, pa obične procedure ostaju brze.

## Jezik i podaci o programu

- **Postavke → Jezik:** hrvatski, engleski ili njemački. Promjena vrijedi odmah i pamti se za sljedeće pokretanje. Pri prvom pokretanju jezik se bira prema jeziku Windowsa.
- **O programu:** verzija, kratki opis i autor (Made by Dorijan J.).
- Prijevodi su u `src\AutomatedMacro\Core\Strings.cs` (ključ → hrvatski, engleski, njemački).

## Datoteke

- Procedure se spremaju u mapu `Makroi` pokraj exe datoteke (mapu možeš promijeniti u **Postavke**). Popis lijevo prikazuje tu mapu: klik otvara proceduru, desni klik nudi preimenovanje, dupliciranje, novu mapu itd.
- Obrisane procedure idu u **Koš**, odakle ih možeš vratiti.
- Format datoteke je čitljivi JSON (`.macro`).

## Dobro je znati

- Koordinate su apsolutni pikseli ekrana, pa ciljni prozor drži na istom mjestu i iste veličine kao pri snimanju.
- Ako ciljni program radi kao administrator, i Automated Macro treba pokrenuti kao administrator. Inače Windows blokira klikove.
- Neke igre sa zaštitom od varanja ignoriraju simulirani unos.
- Nasumični tekst je jedinstven dok je program otvoren. Svaka generirana vrijednost upisuje se u zapisnik.
- Tekst označen kao skriven (•••) skriven je samo u prikazu. U `.macro` datoteci je i dalje čitljiv.

## Gradnja iz izvornog koda

Treba .NET 10 SDK. Naredba sprema prijenosni `AutomatedMacro.exe` (oko 60 MB) u korijen repozitorija:

```
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

S parametrom `-Small` nastaje mali exe (oko 0,5 MB) kojem na računalu treba instaliran .NET 10 Desktop Runtime.

Izvorni kod je u `src\AutomatedMacro` (C#, WPF, .NET 10).
