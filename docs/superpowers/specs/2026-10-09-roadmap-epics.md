# Eepokset: äänet ja skenaariot

Käyttäjä pyysi 9.10.2026 kahta uutta kokonaisuutta: monipuolisempia skenaarioita ja ääniä. Molemmista tehdään oma
eepoksensa. Eepos jakautuu vaiheisiin, ja jokainen vaihe saa oman specin, suunnitelman ja mergen samaan tapaan kuin
vaiheet 4a–4s.

## Järjestys

1. **Äänet** ensin. Se ei riipu skenaarioista ja on pienempi kokonaisuus. Vaihe 5a (äänen ydin ja laukaukset) on valmis
   9.10.2026.
2. **Taktiikat** seuraavaksi (käyttäjän valinta 9.10.2026). Hyökkäykset näyttävät nyt itsemurhatehtäviltä, joten tämä
   tekee nykyisestä tehtävästä pelattavan.
3. **Skenaariot** sen jälkeen. Tapahtumat ja laukaisimet tehdään ensin, koska kaikki muu nojaa niihin.
4. **Kehitys ja kampanja** viimeisenä: miesten kokemus ja kehittyminen, oma ryhmä roolipelin tapaan ja tarina, joka
   koostuu useasta tehtävästä. Ne merkitsevät jotain vasta, kun tehtäviä on useita.

## Eepos 1: Äänet

- **Äänitehosteet:** laukaukset, räjähdykset, osumat, huudot, liike ja ympäristö.
- **Musiikki:** valikko, briefing ja taistelu.
- **Asetusikkuna:** äänenvoimakkuudet, kieli (nykyinen `--lang`) ja muut asetukset. Ikkuna liitetään Esc-valikkoon ja
  tallennetaan samaan tapaan kuin `user://progress.json`.

Reunaehdot:

- Äänet eivät saa paljastaa vihollista. Pelaaja kuulee vain sen, minkä oma puoli kuulee (VisionSystem.UpdateHearing,
  CombatEffects.AddSignals).
- Äänilogiikka pysyy clientissä, jolloin simulaatio pysyy deterministisenä. Tapahtumien muunnos ääniksi on testattavaa
  koodia Nmf.Clientissä, ja Godotin toisto on ohut kerros sen päällä.
- Äänten on kestettävä nopeus x8 ja tauko. Samanaikaisia ääniä rajoitetaan, ja tauko pysäyttää tehosteet.
- Lisenssit ovat CC0 tai CC BY-SA, ja jokaisen tiedoston lähde kirjataan.

## Eepos 2: Taktiikat

Käyttäjä 9.10.2026: miesten pitää tuplaklikkauksesta etsiä paras tapa hyökätä eikä vain kävellä päin ja kuolla.
Hyökkäyssuunnitelma on pelaajan muokattavissa, ja se riippuu tehtävästä ja miehistä.

- Taktiikat ovat dataa, kuten aseet: esimerkiksi tuli ja liike (tukiasema ja koukkaus), soluttautuminen, pihtiliike ja
  pitkä tuli.
- Suunnittelija ehdottaa parasta maaston, vihollisen ja miesten ominaisuuksien perusteella. Pelaaja voi vaihtaa taktiikkaa
  ja muokata suunnitelmaa (tuliasema, reitit) kartalla.
- Tehtävä voi suosia tai kieltää taktiikoita.
- Vihollinen käyttää samaa suunnittelijaa (aiempi "fiksumpi vihollinen").
- Nykytilan heikkoudet (selvitys 9.10.2026): ei tukiasemaa eikä koukkausta, heikko suojatuli, syöksyjät eivät reagoi
  tuleen, rynnäkkö lähtee lamauttamattomaan kohteeseen ja hyökkäys jatkuu seuraavaan viholliseen.

## Eepos 3: Skenaariot

Esimerkkitehtävä: vihollisen linjojen taakse pudonneen lentokoneen hylky etsitään, ja haavoittunut lentäjä tuodaan
takaisin omien linjojen luo. Tehtävä painottuu talveen. Jokainen skenaario saa tarvittaessa oman karttansa.

Vaiheet riippuvuusjärjestyksessä:

1. **Tapahtumat ja laukaisimet.** Pääspecin §10.3 "kun … niin …" -tapahtumat sekä uudet tavoitetyypit: etsi alue,
   löydä kohde, pelasta yksikkö ja tuo se takaisin.
2. **Haavoittuneen kantaminen.** Lentäjä on yksikkö. Kantajia on yksi tai kaksi, tai talvella käytetään ahkiota.
   Kantaminen hidastaa liikettä, ja jos kantaja kaatuu, tavoite palautuu kuten `pick_up`-tavoitteessa.
3. **Kranaatinheitintuki** (§8.7): radiomies, tulenjohto, viive, tähystyslaukaus ja korjaukset sekä rajalliset
   ammukset.
4. **Monimutkaisempi vihollinen** (§9.2–9.3): partioreitit, valppaustilat, hälytyksen leviäminen ja etsintäpartio, joka
   kilpailee pelaajan kanssa hylyn luo.
5. **Talvi ja karttaputki:** lumimaasto ja sen grafiikka, upottava lumi, sukset ja jäljet lumessa pelimekaniikkana.
   Lisäksi mapgen yleistetään niin, että jokaiselle skenaariolle saa oman alueen (`tools/mapgen/areas.py`).
6. **Hylkytehtävä:** kartta, joukot, briefing ja tasapaino.

Reunaehdot:

- Talvi kumoaa pääspecin §1.4:n rajauksen. Jos tehtävä on hämärässä tai yöllä, valo vaikuttaa havaitsemiseen (§7.3).
- Satelliittiaukot (4p) eivät toimi talvikuvilla. Maankäyttö otetaan kesäkuvista ja lumi piirretään päälle.
- Etsintätehtävässä opasnuoli (4l) ei saa näyttää hylyn paikkaa ennen kuin joku on nähnyt sen.

## Eepos 4: Kehitys ja kampanja

- **Miesten kehitys:** kokemuspisteitä tulee tehtävistä (selviytyminen, osumat, tavoitteet, johtaminen). Taidot kasvavat
  (tarkkuus, kokemus, hermo, johtajuus), ja mukaan tulevat erikoistumiset, kuten tarkka-ampuja, partiomies, pioneeri ja
  lääkintämies. Haavat ja kuolema seuraavat tehtävästä toiseen.
- **Oma ryhmä:** pelaaja kokoaa joukostaan "dream teamin" roolipelin tapaan: värvää, valitsee miehet ja aseet tehtävään
  ja kehittää heitä.
- **Tarina:** tehtävävalikkoon kokonainen tarina, joka koostuu useasta erillisestä tehtävästä.
