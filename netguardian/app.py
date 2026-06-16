from __future__ import annotations

import queue
import tkinter as tk
from tkinter import messagebox, simpledialog, ttk

from .models import ConnectivityReport, MonitorPolicy, NetworkState, WifiProfile
from .monitor import MonitorEvent, MonitorEventType, MonitorWorker
from .settings import SettingsStore
from .wifi import WifiManager


STATE_TEXT = {
    NetworkState.HEALTHY: "Healthy",
    NetworkState.SUSPECT: "Suspect",
    NetworkState.DNS_ISSUE: "DNS issue",
    NetworkState.GATEWAY_ISSUE: "Gateway issue",
    NetworkState.INTERNET_ISSUE: "Internet issue",
    NetworkState.DISCONNECTED: "Disconnected",
    NetworkState.RECOVERING: "Recovering",
    NetworkState.FAILED: "Failed",
}


class NetGuardianApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("NetGuardian")
        self.geometry("980x640")
        self.minsize(880, 560)

        self.store = SettingsStore()
        self.policy, self.profiles = self.store.load()
        self.events: queue.Queue[MonitorEvent] = queue.Queue()
        self.worker = MonitorWorker(self.policy, self.profiles, self.store, self.events)
        self.wifi = WifiManager()

        self._build_style()
        self._build_ui()
        self._refresh_policy_fields()
        self._refresh_profiles()
        self.after(250, self._poll_events)

    def _build_style(self) -> None:
        style = ttk.Style(self)
        if "vista" in style.theme_names():
            style.theme_use("vista")
        style.configure("Title.TLabel", font=("Segoe UI", 18, "bold"))
        style.configure("Status.TLabel", font=("Segoe UI", 22, "bold"))
        style.configure("Muted.TLabel", foreground="#666")
        style.configure("Danger.TButton", foreground="#9a1b1b")

    def _build_ui(self) -> None:
        root = ttk.Frame(self, padding=16)
        root.pack(fill=tk.BOTH, expand=True)

        header = ttk.Frame(root)
        header.pack(fill=tk.X)
        ttk.Label(header, text="NetGuardian", style="Title.TLabel").pack(side=tk.LEFT)
        ttk.Button(header, text="Start monitor", command=self._start_monitor).pack(side=tk.RIGHT, padx=(8, 0))
        ttk.Button(header, text="Stop", command=self._stop_monitor).pack(side=tk.RIGHT)
        ttk.Button(header, text="Check now", command=self._check_now).pack(side=tk.RIGHT, padx=(0, 8))

        self.notebook = ttk.Notebook(root)
        self.notebook.pack(fill=tk.BOTH, expand=True, pady=(16, 0))

        self._build_status_tab()
        self._build_wifi_tab()
        self._build_policy_tab()
        self._build_tools_tab()
        self._build_log_tab()

    def _build_status_tab(self) -> None:
        tab = ttk.Frame(self.notebook, padding=16)
        self.notebook.add(tab, text="Status")

        self.status_label = ttk.Label(tab, text="Not checked", style="Status.TLabel")
        self.status_label.pack(anchor=tk.W)
        self.summary_label = ttk.Label(tab, text="Start monitoring or run a manual check.", style="Muted.TLabel")
        self.summary_label.pack(anchor=tk.W, pady=(4, 16))

        details = ttk.LabelFrame(tab, text="Network")
        details.pack(fill=tk.X)
        self.detail_vars = {
            "ssid": tk.StringVar(value="-"),
            "ipv4": tk.StringVar(value="-"),
            "gateway": tk.StringVar(value="-"),
            "checked_at": tk.StringVar(value="-"),
        }
        for row, (label, var_name) in enumerate(
            [("Wi-Fi", "ssid"), ("IPv4", "ipv4"), ("Gateway", "gateway"), ("Last check", "checked_at")]
        ):
            ttk.Label(details, text=label).grid(row=row, column=0, sticky=tk.W, padx=12, pady=6)
            ttk.Label(details, textvariable=self.detail_vars[var_name]).grid(row=row, column=1, sticky=tk.W, padx=12, pady=6)

        probes = ttk.LabelFrame(tab, text="Probes")
        probes.pack(fill=tk.BOTH, expand=True, pady=(16, 0))
        self.probe_tree = ttk.Treeview(probes, columns=("ok", "latency", "detail"), show="headings", height=8)
        self.probe_tree.heading("ok", text="Result")
        self.probe_tree.heading("latency", text="Latency")
        self.probe_tree.heading("detail", text="Detail")
        self.probe_tree.column("ok", width=120, anchor=tk.W)
        self.probe_tree.column("latency", width=120, anchor=tk.W)
        self.probe_tree.column("detail", width=560, anchor=tk.W)
        self.probe_tree.pack(fill=tk.BOTH, expand=True, padx=8, pady=8)

    def _build_wifi_tab(self) -> None:
        tab = ttk.Frame(self.notebook, padding=16)
        self.notebook.add(tab, text="Wi-Fi")

        actions = ttk.Frame(tab)
        actions.pack(fill=tk.X)
        ttk.Button(actions, text="Scan", command=self._scan_wifi).pack(side=tk.LEFT)
        ttk.Button(actions, text="Add selected", command=self._add_selected_network).pack(side=tk.LEFT, padx=8)
        ttk.Button(actions, text="Add manually", command=self._add_manual_profile).pack(side=tk.LEFT)
        ttk.Button(actions, text="Move up", command=lambda: self._move_profile(-1)).pack(side=tk.RIGHT, padx=(8, 0))
        ttk.Button(actions, text="Move down", command=lambda: self._move_profile(1)).pack(side=tk.RIGHT)

        panes = ttk.PanedWindow(tab, orient=tk.HORIZONTAL)
        panes.pack(fill=tk.BOTH, expand=True, pady=(12, 0))

        scan_frame = ttk.LabelFrame(panes, text="Nearby networks")
        panes.add(scan_frame, weight=1)
        self.scan_tree = ttk.Treeview(scan_frame, columns=("signal", "auth"), show="tree headings")
        self.scan_tree.heading("#0", text="SSID")
        self.scan_tree.column("#0", width=220, anchor=tk.W)
        self.scan_tree.heading("signal", text="Signal")
        self.scan_tree.heading("auth", text="Authentication")
        self.scan_tree.column("signal", width=80)
        self.scan_tree.column("auth", width=160)
        self.scan_tree.pack(fill=tk.BOTH, expand=True, padx=8, pady=8)

        profiles_frame = ttk.LabelFrame(panes, text="Recovery priority")
        panes.add(profiles_frame, weight=2)
        self.profile_tree = ttk.Treeview(
            profiles_frame,
            columns=("priority", "enabled", "failures", "last_success"),
            show="tree headings",
        )
        self.profile_tree.heading("#0", text="SSID")
        self.profile_tree.column("#0", width=220, anchor=tk.W)
        for col, title, width in [
            ("priority", "Priority", 70),
            ("enabled", "Enabled", 80),
            ("failures", "Failures", 80),
            ("last_success", "Last success", 180),
        ]:
            self.profile_tree.heading(col, text=title)
            self.profile_tree.column(col, width=width, anchor=tk.W)
        self.profile_tree.pack(fill=tk.BOTH, expand=True, padx=8, pady=8)

        bottom = ttk.Frame(profiles_frame)
        bottom.pack(fill=tk.X, padx=8, pady=(0, 8))
        ttk.Button(bottom, text="Toggle enabled", command=self._toggle_profile).pack(side=tk.LEFT)
        ttk.Button(bottom, text="Remove", command=self._remove_profile).pack(side=tk.LEFT, padx=8)
        ttk.Button(bottom, text="Connect", command=self._connect_selected_profile).pack(side=tk.RIGHT)

    def _build_policy_tab(self) -> None:
        tab = ttk.Frame(self.notebook, padding=16)
        self.notebook.add(tab, text="Policy")

        form = ttk.LabelFrame(tab, text="Detection and recovery")
        form.pack(fill=tk.X)
        self.policy_vars = {
            "check_interval_seconds": tk.IntVar(),
            "consecutive_failures_before_recovery": tk.IntVar(),
            "recovery_cooldown_seconds": tk.IntVar(),
            "enable_auto_switch": tk.BooleanVar(),
            "enable_current_wifi_reconnect": tk.BooleanVar(),
            "enable_dns_flush": tk.BooleanVar(),
            "enable_wlan_service_restart": tk.BooleanVar(),
            "enable_adapter_restart": tk.BooleanVar(),
        }
        numeric = [
            ("Check interval seconds", "check_interval_seconds"),
            ("Failures before recovery", "consecutive_failures_before_recovery"),
            ("Recovery cooldown seconds", "recovery_cooldown_seconds"),
        ]
        for row, (label, key) in enumerate(numeric):
            ttk.Label(form, text=label).grid(row=row, column=0, sticky=tk.W, padx=12, pady=8)
            ttk.Spinbox(form, from_=1, to=3600, textvariable=self.policy_vars[key], width=12).grid(
                row=row, column=1, sticky=tk.W, padx=12, pady=8
            )

        checks = [
            ("Allow current Wi-Fi soft reconnect", "enable_current_wifi_reconnect"),
            ("Allow switching to priority Wi-Fi list", "enable_auto_switch"),
            ("Flush DNS before recovery", "enable_dns_flush"),
            ("Allow WLAN service restart (reserved)", "enable_wlan_service_restart"),
            ("Allow adapter restart (reserved)", "enable_adapter_restart"),
        ]
        for index, (label, key) in enumerate(checks, start=len(numeric)):
            ttk.Checkbutton(form, text=label, variable=self.policy_vars[key]).grid(
                row=index, column=0, columnspan=2, sticky=tk.W, padx=12, pady=6
            )

        ttk.Button(tab, text="Save policy", command=self._save_policy).pack(anchor=tk.W, pady=16)

    def _build_tools_tab(self) -> None:
        tab = ttk.Frame(self.notebook, padding=16)
        self.notebook.add(tab, text="Tools")
        ttk.Button(tab, text="Run connectivity check", command=self._check_now).pack(anchor=tk.W)
        ttk.Button(tab, text="Scan Wi-Fi networks", command=self._scan_wifi).pack(anchor=tk.W, pady=8)
        ttk.Label(
            tab,
            text="High-risk actions such as WLAN service restart and adapter restart are policy-gated and reserved for a service build.",
            style="Muted.TLabel",
            wraplength=720,
        ).pack(anchor=tk.W, pady=(16, 0))

    def _build_log_tab(self) -> None:
        tab = ttk.Frame(self.notebook, padding=16)
        self.notebook.add(tab, text="Log")
        self.log_text = tk.Text(tab, height=20, wrap=tk.WORD)
        self.log_text.pack(fill=tk.BOTH, expand=True)

    def _start_monitor(self) -> None:
        self._save_policy()
        self.worker.start()

    def _stop_monitor(self) -> None:
        self.worker.stop()

    def _check_now(self) -> None:
        report = self.worker.run_once()
        self._apply_report(report)

    def _scan_wifi(self) -> None:
        self._log("Scanning nearby Wi-Fi networks.")
        for row in self.scan_tree.get_children():
            self.scan_tree.delete(row)
        for network in self.wifi.scan():
            self.scan_tree.insert(
                "",
                tk.END,
                iid=network.ssid,
                text=network.ssid,
                values=(
                    "" if network.signal_percent is None else f"{network.signal_percent}%",
                    network.authentication or "-",
                ),
            )

    def _add_selected_network(self) -> None:
        selection = self.scan_tree.selection()
        if not selection:
            messagebox.showinfo("NetGuardian", "Select a nearby Wi-Fi network first.")
            return
        self._add_profile(selection[0])

    def _add_manual_profile(self) -> None:
        ssid = simpledialog.askstring("Add Wi-Fi", "SSID:")
        if ssid:
            self._add_profile(ssid.strip())

    def _add_profile(self, ssid: str) -> None:
        if any(profile.ssid == ssid for profile in self.profiles):
            messagebox.showinfo("NetGuardian", "This Wi-Fi is already in the list.")
            return
        password = simpledialog.askstring(
            "Wi-Fi password",
            "Password, leave blank for open or already-saved network:",
            show="*",
        )
        if password is not None and password:
            if not self.wifi.add_profile(ssid, password):
                messagebox.showwarning("NetGuardian", "Windows WLAN profile could not be created.")
        self.profiles.append(WifiProfile(ssid=ssid, priority=len(self.profiles) + 1))
        self._renumber_profiles()
        self._save()
        self._refresh_profiles()

    def _selected_profile(self) -> WifiProfile | None:
        selection = self.profile_tree.selection()
        if not selection:
            return None
        ssid = selection[0]
        return next((profile for profile in self.profiles if profile.ssid == ssid), None)

    def _toggle_profile(self) -> None:
        profile = self._selected_profile()
        if not profile:
            return
        profile.enabled = not profile.enabled
        self._save()
        self._refresh_profiles()

    def _remove_profile(self) -> None:
        profile = self._selected_profile()
        if not profile:
            return
        self.profiles = [item for item in self.profiles if item.ssid != profile.ssid]
        self.worker.profiles = self.profiles
        self._renumber_profiles()
        self._save()
        self._refresh_profiles()

    def _move_profile(self, direction: int) -> None:
        profile = self._selected_profile()
        if not profile:
            return
        ordered = sorted(self.profiles, key=lambda item: item.priority)
        index = ordered.index(profile)
        new_index = index + direction
        if not 0 <= new_index < len(ordered):
            return
        ordered[index], ordered[new_index] = ordered[new_index], ordered[index]
        self.profiles = ordered
        self.worker.profiles = self.profiles
        self._renumber_profiles()
        self._save()
        self._refresh_profiles()
        self.profile_tree.selection_set(profile.ssid)

    def _connect_selected_profile(self) -> None:
        profile = self._selected_profile()
        if not profile:
            return
        self._log(f"Manual connect requested for {profile.ssid}.")
        if self.wifi.connect(profile.ssid):
            messagebox.showinfo("NetGuardian", "Connection command sent.")
        else:
            messagebox.showwarning("NetGuardian", "Connection command failed.")

    def _save_policy(self) -> None:
        for key, var in self.policy_vars.items():
            setattr(self.policy, key, var.get())
        self.policy.normalize()
        self._refresh_policy_fields()
        self._save()
        self._log("Policy saved.")

    def _save(self) -> None:
        self.store.save(self.policy, self.profiles)

    def _refresh_policy_fields(self) -> None:
        for key, var in self.policy_vars.items():
            var.set(getattr(self.policy, key))

    def _refresh_profiles(self) -> None:
        for row in self.profile_tree.get_children():
            self.profile_tree.delete(row)
        for profile in sorted(self.profiles, key=lambda item: item.priority):
            self.profile_tree.insert(
                "",
                tk.END,
                iid=profile.ssid,
                text=profile.ssid,
                values=(
                    profile.priority,
                    "Yes" if profile.enabled else "No",
                    profile.failure_count,
                    profile.last_success_at or "-",
                ),
            )

    def _renumber_profiles(self) -> None:
        for index, profile in enumerate(self.profiles, start=1):
            profile.priority = index

    def _poll_events(self) -> None:
        while True:
            try:
                event = self.events.get_nowait()
            except queue.Empty:
                break
            if event.type == MonitorEventType.REPORT:
                self._apply_report(event.payload)  # type: ignore[arg-type]
            elif event.type == MonitorEventType.LOG:
                self._log(str(event.payload))
            elif event.type == MonitorEventType.PROFILES_CHANGED:
                self._refresh_profiles()
        self.after(250, self._poll_events)

    def _apply_report(self, report: ConnectivityReport) -> None:
        self.status_label.configure(text=STATE_TEXT.get(report.state, report.state.value))
        self.summary_label.configure(text=report.summary or "-")
        self.detail_vars["ssid"].set(report.current_ssid or "-")
        self.detail_vars["ipv4"].set(report.ipv4 or "-")
        self.detail_vars["gateway"].set(report.gateway or "-")
        self.detail_vars["checked_at"].set(report.checked_at)
        for row in self.probe_tree.get_children():
            self.probe_tree.delete(row)
        for probe in report.probes:
            latency = "-" if probe.latency_ms is None else f"{probe.latency_ms:.0f} ms"
            self.probe_tree.insert("", tk.END, values=("OK" if probe.ok else "Fail", latency, f"{probe.name}: {probe.detail}"))

    def _log(self, message: str) -> None:
        self.log_text.insert(tk.END, message + "\n")
        self.log_text.see(tk.END)

    def destroy(self) -> None:
        self.worker.stop()
        super().destroy()


def main() -> None:
    app = NetGuardianApp()
    app.mainloop()
