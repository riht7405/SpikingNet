# Spiking Net

Минимальная спайковая нейронная сеть на C#/.NET с:
- LIF-нейронами (Leaky Integrate-and-Fire)
- Синапсами с задержкой (кольцевой буфер)
- STDP-пластичностью (обучение)
- Нейромодулятором (дофамин)
- WinForms-визуализацией в реальном времени

## Запуск
Открыть `SpikingNet.sln` в Visual Studio 2022+, нажать F5.

## Архитектура
- `Core/Neuron.cs` — модель нейрона
- `Core/Synapse.cs` — синапс с задержкой и STDP
- `Core/Network.cs` — структура сети
- `Core/Simulation.cs` — движок времени
- `Core/Neuromodulator.cs` — дофамин
- `Rendering/MainForm.cs` — визуализация