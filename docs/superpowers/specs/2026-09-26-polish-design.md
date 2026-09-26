# No Man's Forest – viimeistely (vaihe 4o)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("jatka seuraavat vaiheet itsenäisesti loppuun, kunnes tekemistä ei enää ole"). Vaiheessa korjataan aiemmista vaiheista jääneet pikkuviat.

## 1. Suomenkielinen käyttöliittymä

Kun pelin kieli on suomi (`--lang=fi` tai valikon valinta), seuraavat tekstit näkyvät suomeksi:
- **Yläpalkin tilarivi** (`HudText.Status`): vihollinen nähty, kuultu ja muistissa; tappiot; vihollisia maassa; komennossa; paperit; F1-ohje.
- **Kortit** (`UnitStatus` … `language`): tila, kunto ja alarivi. Esimerkiksi "Seisoo", "Ehjä" ja "5+2 · 1 kr · Vapaa tuli".
- **F1-ohje.**
- **Tutkimisen ilmoitus** (`LootText`), esimerkiksi "+2 lipasta +1 kranaatti". Tehtävän esineet näkyvät tehtävän omalla suomenkielisellä nimellä.

## 2. Pikkuviat

- **Ryhmien järjestys:** ryhmien numerot (1, 2, …) määräytyvät `squads:`-listan järjestyksestä eivätkä siitä, missä järjestyksessä ryhmät esiintyvät kokoonpanossa.
- **Automaattinen tutkiminen:**
  - Mies lähtee tutkimaan vain kaatunutta, jonka oma puoli on nähnyt.
  - Käskystä asentoon mennyt mies ei lähde tutkimaan omin päin, eikä myöskään aluetulta ampuva mies.
- **Näppäimet:**
  - Numerot ja Z/X/C luetaan fyysisen paikan mukaan, jolloin ne toimivat myös muilla näppäimistöasetteluilla.
  - Ctrl/Cmd + Z/X/C ei anna asentokäskyä.
- **Kranaatti näkymättömältä heittäjältä:** vain lennon loppu piirretään, jolloin kaari ei paljasta heittäjän paikkaa.

## 3. Testaus

- **Client:** suomenkieliset kortit ja jokaiselle tilalle käännös, tutkimisen ilmoitus suomeksi, tilarivi molemmilla kielillä.
- **Content:** ryhmien numerointi määrittelyjärjestyksessä.
- **Sim:** tuntematonta kaatunutta ei tutkita, eikä käskystä asentoon mennyt mies lähde tutkimaan.
