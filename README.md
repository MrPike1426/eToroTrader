# TopStepTrader
AI Augmented TopStep API Trader

## Overview

TopStepTrader is a sophisticated WPF desktop application designed to facilitate AI-augmented trading on the TopStep API. The application provides technical analysis, automated backtesting, and live trading capabilities with built-in risk management.

---

## Current Build - Architecture

### Project Structure

The solution is organized into a clean layered architecture:

```
src/
├── TopStepTrader.Core/          # Domain models and interfaces
│   ├── Models/                   # TrendAnalysisResult, BalanceHistory, etc.
│   └── Interfaces/               # Service contracts (IBalanceHistoryService, etc.)
├── TopStepTrader.Data/           # Data access layer
│   ├── AppDbContext.vb           # EF Core database context
│   └── Repositories/             # OrderRepository, etc.
├── TopStepTrader.Services/       # Business logic layer
│   ├── Trading/                  # TrendAnalysisService
│   ├── Market/                   # BalanceHistoryService
│   └── ServicesExtensions.vb     # Dependency injection registration
├── TopStepTrader.API/            # TopStep API integration
├── TopStepTrader.ML/             # Machine learning models (future)
└── TopStepTrader.UI/             # WPF presentation layer
    ├── Views/                    # XAML windows and user controls
    │   ├── MainWindow.xaml       # Main application shell
    │   ├── DashboardView.xaml    # Dashboard
    │   ├── SettingsView.xaml     # Settings
    │   └── BacktestView.xaml     # Backtest & Test Trade
    ├── ViewModels/               # MVVM view models
    ├── Styles/                   # XAML resource dictionaries (Colors, Buttons, etc.)
    └── Infrastructure/           # AppBootstrapper, DI container setup
```

### Technology Stack

- **Language:** Visual Basic .NET (VB.NET)
- **UI Framework:** WPF (Windows Presentation Foundation)
- **Database:** Entity Framework Core with local database
- **Architecture Pattern:** MVVM (Model-View-ViewModel)
- **DI Container:** Dependency Injection (built-in .NET)

---

## Features

### Implemented (Current Build)

#### 1. **Dashboard** 📊
- Real-time account balance overview
- Balance history tracking with BalanceHistoryService
- Connection status indicator
- Account performance metrics

#### 2. **Test Trade** 🧪
- EMA/RSI trend analysis on 24-hour bar data
- Combined technical indicator scoring:
  - EMA 21 vs EMA 50 crossover (25% weight)
  - Price position relative to EMAs (35% combined)
  - RSI 14 momentum analysis (20% weight)
  - EMA momentum & candle pattern (20% combined)
- Up/Down probability percentages
- **Test BUY** and **Test SELL** buttons for paper trading
- Deterministic analysis (zero AI token cost)
- See [TICKET-001_TestTrade_EMA_RSI_TrendAnalysis.md](./Manus_Tickets/TICKET-001_TestTrade_EMA_RSI_TrendAnalysis.md) for technical details

#### 3. **Backtest** 🔬
- Run historical backtests on trading strategies
- View previous backtest results
- Test Trade tab integration (see above)

#### 4. **Settings** ⚙️
- Application configuration management

### Planned / To Do

- **Market Data** 📈 — Real-time market data and charting
- **AI Signals** 🤖 — AI-powered trading signals
- **Orders** 📋 — Order management and history
- **Risk Guard** 🛡️ — Risk management and position sizing rules

---

## Navigation

The main application window (`MainWindow.xaml`) features a sidebar navigation panel with:

**Active Sections:**
- Dashboard
- Test Trade  
- Backtest

**To Do Sections:**
- Market Data
- AI Signals
- Orders
- Risk Guard

**Status Indicator:**
- Connection status to TopStep API (bottom of sidebar)

---

## Services & Components

### Core Services

#### **TrendAnalysisService**
- **Location:** `src/TopStepTrader.Services/Trading/TrendAnalysisService.vb`
- **Purpose:** Analyzes 24-hour market trends using EMA and RSI indicators
- **Returns:** `TrendAnalysisResult` with Up/Down probabilities and indicator values
- **DI Registration:** Registered in `ServicesExtensions.vb`

#### **BalanceHistoryService**
- **Location:** `src/TopStepTrader.Services/Market/BalanceHistoryService.vb`
- **Purpose:** Tracks and retrieves account balance history
- **Interface:** `IBalanceHistoryService` (Core layer)
- **DI Registration:** Registered in `ApplicationServiceExtensions.vb`

#### **OrderRepository**
- **Location:** `src/TopStepTrader.Data/Repositories/OrderRepository.vb`
- **Purpose:** Data access for orders
- **Context:** Uses `AppDbContext`

### Data Models

#### **TrendAnalysisResult**
```vb
Public Class TrendAnalysisResult
    Public Property UpProbability As Double
    Public Property DownProbability As Double
    Public Property EMA21 As Single
    Public Property EMA50 As Single
    Public Property RSI14 As Single
    Public Property LastClose As Decimal
    Public Property Summary As String
    Public Property BarsAnalysed As Integer
    Public Property AnalysedAt As DateTimeOffset
    Public Property Signals As List(Of String)
End Class
```

#### **BalanceHistoryEntity**
- Tracks historical account balance snapshots
- Used by `BalanceHistoryService` for chart data

---

## Getting Started

### Prerequisites
- .NET 6.0 or higher
- Visual Studio 2022
- LocalDB (for local database)

### Build & Run
```powershell
# Restore packages
dotnet restore

# Build solution
dotnet build

# Run UI project
dotnet run --project src/TopStepTrader.UI/TopStepTrader.UI.vbproj
```

### Configuration
- Database connection string configured in `AppDbContext.vb`
- API credentials configured in `SettingsView.xaml` / `SettingsViewModel.vb`

---

## Recent Changes (Latest Build)

### Navigation Panel Update
- Reorganized sidebar with active and "To Do" sections
- Added connection status indicator
- Improved visual hierarchy and emoji indicators

### Test Trade (TICKET-001)
- Implemented EMA/RSI trend analysis
- Added Test BUY/SELL buttons
- Integrated with BacktestView Tab 3
- See [TICKET-001](./Manus_Tickets/TICKET-001_TestTrade_EMA_RSI_TrendAnalysis.md) for full technical specification

### Dashboard Enhancement
- Added BalanceHistoryService integration
- Real-time balance tracking

---

## Development

### Project Guidelines

- **Language:** VB.NET (all new code in VB.NET)
- **Pattern:** MVVM for UI layer
- **Data:** Entity Framework Core with repository pattern
- **Testing:** Unit tests for services and repositories
- **Documentation:** Technical specs in `Manus_Tickets/` folder

### Adding a New Feature

1. Create model in `TopStepTrader.Core/Models/`
2. Create service interface in `TopStepTrader.Core/Interfaces/`
3. Implement service in `TopStepTrader.Services/`
4. Register in `ServicesExtensions.vb`
5. Create ViewModel in `TopStepTrader.UI/ViewModels/`
6. Create View in `TopStepTrader.UI/Views/`
7. Add navigation button in `MainWindow.xaml`
8. Document in `Manus_Tickets/TICKET-XXX.md`

---

## Tickets & Technical Documentation

- [TICKET-001: Test Trade EMA/RSI Trend Analysis](./Manus_Tickets/TICKET-001_TestTrade_EMA_RSI_TrendAnalysis.md)

---

## License

Proprietary. All rights reserved.

---

## Support & Contribution

For questions, issues, or contributions, please contact the development team or file an issue in the GitHub repository.

