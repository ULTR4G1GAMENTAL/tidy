# Tidy the Zoo (co-op, Unity 6)

Cok oyunculu 'hayvanat bahcesini topla' oyunu.

## Acmak icin
1. **Unity 6000.3.27f1** (Unity Hub'dan ayni surum) ile bu klasoru proje olarak ac. Ilk acilista paketler inip derleme yapilir (birkac dakika).
2. Sahne dosyasi (`ZooMain.unity`) 2+ GB oldugu icin depoda YOK. Menuden **Zoo > Build Zoo** calistir; sahne koddan uretilir (`Assets/Editor/ZooBuilder.cs`, `ZooEnv.cs`, `ZooGrass.cs`, `ZooProps.cs`, `ZooTrees.cs`) ve `Assets/Scenes/ZooMain.unity` olarak kaydedilir. Play modundayken calistirma; birkac dakika surer.
3. Play'e bas.

## Birlikte calisma kurallari
- Dunyayi degistirmek icin sahneyi elle duzenleme yerine **kodu** duzenle (sahne her build'de sifirdan kurulur, elle yapilan degisiklikler kaybolur).
- Sahne git'e girmez; `.unity` merge cakismasi olmaz. Kod ve asset dosyalarini (`.meta` dahil) commit et.
- Her calismadan once `git pull`, bitince `git push`. Ayni dosyayi (ozellikle `ZooEnv.cs`) ayni anda duzenlemekten kacin; kucuk commit'ler at.
- Yeni 3D asset eklerken lisansini kontrol et ve `Assets/Zoo/CREDITS_models.txt` dosyasina yaz. Ticari yayin icin CC-BY-NC / lisansi belirsiz asset'ler cikarilmali (CREDITS dosyasina bak).
- Tek dosya 100 MB'i gecemez (GitHub siniri). Buyuk modelleri kucult.

## Klasorler
- `Assets/Editor` : dunya kurucu kodlari (menu: Zoo)
- `Assets/Env`, `Assets/Zoo`, `Assets/Trees`, `Assets/Animals` : modeller, dokular, malzemeler, golgeleyiciler
- `Assets/Scripts` : oyun kodlari
