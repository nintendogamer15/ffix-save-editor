%{!?app_version:%global app_version 0.0.0}
%global debug_package %{nil}
%global _build_id_links none
%global __strip /bin/true
%global source_date_epoch_from_changelog 0

Name:           ffix-save-editor
Version:        %{app_version}
Release:        1
Summary:        Cross-platform Final Fantasy IX save editor
License:        MIT
URL:            https://github.com/nintendogamer15/ffix-save-editor
Source0:        app-binary
Source1:        ffix-save-editor.desktop
Source2:        io.github.nintendogamer15.FFIXSaveEditor.metainfo.xml
Source3:        icon.png
Source4:        LICENSE
Source5:        NOTICES.md

ExclusiveArch:  x86_64
BuildRequires:  appstream
BuildRequires:  desktop-file-utils
Requires:       ca-certificates
Requires:       fontconfig
Requires:       glibc
Requires:       hicolor-icon-theme
Requires:       krb5-libs
Requires:       libgcc
Requires:       libICE
Requires:       libicu
Requires:       libSM
Requires:       libstdc++
Requires:       libX11
Requires:       openssl-libs
Requires:       tzdata
Requires:       zlib-ng-compat
Recommends:     xdg-desktop-portal

%description
A self-contained Avalonia desktop editor for supported Final Fantasy IX
PlayStation, PC rerelease, and Memoria mod save formats.

%prep

%build

%install
install -Dm0755 %{SOURCE0} %{buildroot}%{_bindir}/ffix-save-editor
desktop-file-install --dir=%{buildroot}%{_datadir}/applications %{SOURCE1}
install -Dm0644 %{SOURCE2} \
  %{buildroot}%{_datadir}/metainfo/io.github.nintendogamer15.FFIXSaveEditor.metainfo.xml
install -Dm0644 %{SOURCE3} \
  %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/ffix-save-editor.png
install -Dm0644 %{SOURCE4} %{buildroot}%{_licensedir}/%{name}/LICENSE
install -Dm0644 %{SOURCE5} %{buildroot}%{_docdir}/%{name}/NOTICES.md

%check
desktop-file-validate %{buildroot}%{_datadir}/applications/ffix-save-editor.desktop
appstreamcli validate --no-net \
  %{buildroot}%{_datadir}/metainfo/io.github.nintendogamer15.FFIXSaveEditor.metainfo.xml

%files
%{_bindir}/ffix-save-editor
%{_datadir}/applications/ffix-save-editor.desktop
%{_datadir}/icons/hicolor/256x256/apps/ffix-save-editor.png
%{_datadir}/metainfo/io.github.nintendogamer15.FFIXSaveEditor.metainfo.xml
%license %{_licensedir}/%{name}/LICENSE
%doc %{_docdir}/%{name}/NOTICES.md

%changelog
* Tue Aug 18 2026 Robert - 0.3.3-1
- Add native Fedora packaging for the self-contained application.
