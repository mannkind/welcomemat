"use strict";
// Throw on a missing id, like the unguarded lookups did before types.
function requireEl(id) {
    const el = document.getElementById(id);
    if (!el)
        throw new Error('Missing required element #' + id);
    return el;
}
// Set the theme before first paint; otherwise dark users get a light flash.
(function () {
    const t = localStorage.getItem('theme') ||
        (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    document.documentElement.setAttribute('data-theme', t);
})();
// Noon, so a timezone shift can't drag the date back a day.
function formatDate(str) {
    const d = new Date(str + 'T12:00:00');
    return d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
}
function xsrfToken() {
    const m = document.querySelector('meta[name="xsrf-token"]');
    return m?.getAttribute('content') ?? '';
}
function toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme');
    const next = current === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    localStorage.setItem('theme', next);
    const btn = document.getElementById('theme-toggle');
    if (btn)
        btn.textContent = next === 'dark' ? '☀' : '☾';
}
function copyAddress(text, btn) {
    if (!navigator.clipboard) {
        return;
    }
    navigator.clipboard
        .writeText(text)
        .then(function () {
        const orig = btn.textContent;
        btn.textContent = 'Copied!';
        setTimeout(function () {
            btn.textContent = orig;
        }, 2000);
    })
        .catch(function () { });
}
document.addEventListener('DOMContentLoaded', function () {
    const themeBtn = document.getElementById('theme-toggle');
    if (themeBtn) {
        themeBtn.textContent =
            document.documentElement.getAttribute('data-theme') === 'dark' ? '☀' : '☾';
        themeBtn.addEventListener('click', toggleTheme);
    }
    document.querySelectorAll('.nav-dropdown').forEach(function (dropdown) {
        dropdown.addEventListener('toggle', function () {
            const menu = dropdown.querySelector('.nav-dropdown-content');
            if (!menu)
                return;
            menu.style.right = '';
            if (!dropdown.open)
                return;
            const margin = 8;
            const left = menu.getBoundingClientRect().left;
            if (left < margin)
                menu.style.right = left - margin + 'px';
        });
    });
    document.addEventListener('click', function (e) {
        const target = e.target;
        const btn = target?.closest('.copy-address-btn');
        if (btn?.dataset.address)
            copyAddress(btn.dataset.address, btn);
    });
    const lightboxModal = document.getElementById('lightbox-modal');
    const lightboxClose = document.getElementById('lightbox-close');
    if (lightboxModal && lightboxClose) {
        lightboxClose.addEventListener('click', function () {
            lightboxModal.close();
        });
    }
    if (lightboxModal) {
        lightboxModal.addEventListener('click', function (e) {
            if (e.target === lightboxModal)
                lightboxModal.close();
        });
    }
    document.addEventListener('error', function (e) {
        const img = e.target;
        if (img &&
            img.classList &&
            img.classList.contains('property-card-thumb') &&
            img.dataset.fallbackSrc) {
            const fallback = img.dataset.fallbackSrc;
            img.removeAttribute('data-fallback-src');
            img.src = fallback;
        }
    }, true);
    const opacityInput = document.getElementById('path-opacity');
    const opacityDisplay = document.getElementById('opacity-display');
    if (opacityInput && opacityDisplay) {
        opacityInput.addEventListener('input', function () {
            opacityDisplay.textContent = parseFloat(opacityInput.value).toFixed(1);
        });
    }
    if (typeof L !== 'undefined')
        initPropertyMap();
});
async function adminFetch(form) {
    try {
        const resp = await fetch(form.action, {
            method: 'POST',
            headers: { 'X-Requested-With': 'fetch' },
            body: new FormData(form),
        });
        return {
            ok: resp.ok,
            redirect: resp.headers.get('X-Redirect'),
            error: resp.ok ? '' : (await resp.text()).trim() || 'Something went wrong.',
        };
    }
    catch (_) {
        return { ok: false, redirect: null, error: 'Network error. Please try again.' };
    }
}
async function adminSubmit(form) {
    const r = await adminFetch(form);
    if (r.ok) {
        if (r.redirect)
            window.location.href = r.redirect;
        else
            window.location.reload();
        return '';
    }
    return r.error;
}
async function adminRowSubmit(form) {
    const err = await adminSubmit(form);
    if (err)
        alert(err);
}
// ThisType gets $refs typed inside the methods; Alpine supplies it at runtime.
function calendarApp(currentUserId, isAdmin) {
    const app = {
        selectedCheckIn: null,
        selectedCheckOut: null,
        selectedUserId: currentUserId ? String(currentUserId) : '',
        isAdmin: !!isAdmin,
        modalNotes: '',
        modalError: '',
        editReservationId: '',
        editGuestName: '',
        editNotes: '',
        editError: '',
        panelOpen: false,
        panelDate: '',
        panelGuestName: '',
        panelCheckIn: '',
        panelCheckOut: '',
        panelReservationId: '',
        panelCancelUrl: '',
        panelCancelMsg: '',
        guestbookReservationId: null,
        guestbookMessage: '',
        guestbookError: '',
        init: function () {
            const wanted = new URLSearchParams(window.location.search).get('edit');
            if (wanted)
                void this.startEdit(wanted);
        },
        isInRange: function (date) {
            if (!this.selectedCheckIn || !this.selectedCheckOut)
                return false;
            return date > this.selectedCheckIn && date < this.selectedCheckOut;
        },
        nightCount: function () {
            if (!this.selectedCheckIn || !this.selectedCheckOut)
                return '';
            const n = Math.round((new Date(this.selectedCheckOut).getTime() - new Date(this.selectedCheckIn).getTime()) /
                86400000);
            return n + ' night' + (n !== 1 ? 's' : '');
        },
        isSelectableForEdit: function (resId, isBlackout, isPast) {
            if (isBlackout)
                return false;
            if (isPast && !this.isAdmin)
                return false;
            return !resId || resId === this.editReservationId;
        },
        dayClasses: function (date, resId, isBlackout, isPast) {
            const editing = !!this.editReservationId;
            return {
                'selected-start': this.selectedCheckIn === date,
                'selected-end': this.selectedCheckOut === date,
                'selected-range': this.isInRange(date),
                'editing-stay': editing && !!resId && resId === this.editReservationId,
                'edit-locked': editing && !this.isSelectableForEdit(resId, isBlackout, isPast),
            };
        },
        dayClick: function (el) {
            const date = el.dataset.date;
            if (!date)
                return;
            if (this.editReservationId) {
                if (!this.isSelectableForEdit(el.dataset.resId || '', !!el.dataset.blackout, !!el.dataset.past))
                    return;
                if (this.pickRange(date) &&
                    !this.nightsAreFree(this.selectedCheckIn ?? '', this.selectedCheckOut ?? '')) {
                    this.selectedCheckOut = null;
                    this.editError = 'Those dates run through nights that are already taken.';
                }
                return;
            }
            if (el.dataset.cancelUrl) {
                this.openDayPanel(el);
                return;
            }
            if (el.dataset.clickable)
                this.handleDayClick(date);
        },
        pickRange: function (date) {
            if (!this.selectedCheckIn || date <= this.selectedCheckIn) {
                this.selectedCheckIn = date;
                this.selectedCheckOut = null;
                this.editError = '';
                return false;
            }
            this.selectedCheckOut = date;
            return true;
        },
        handleDayClick: function (date) {
            if (this.pickRange(date))
                this.$refs.reservationModal.showModal();
        },
        closeReservationModal: function () {
            this.$refs.reservationModal.close();
            this.selectedCheckIn = null;
            this.selectedCheckOut = null;
            this.selectedUserId = '';
            this.modalNotes = '';
            this.modalError = '';
        },
        submitReservation: async function () {
            const propertyInput = this.$refs.reservationModal.querySelector('[name=property_id]');
            const body = new URLSearchParams({
                check_in: this.selectedCheckIn ?? '',
                check_out: this.selectedCheckOut ?? '',
                notes: this.modalNotes,
                property_id: propertyInput ? propertyInput.value : '',
                user_id: this.selectedUserId,
            });
            try {
                const resp = await fetch('/reservations', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'X-Requested-With': 'fetch',
                        'X-XSRF-TOKEN': xsrfToken(),
                    },
                    body: body.toString(),
                });
                if (resp.ok) {
                    const redirect = resp.headers.get('X-Redirect');
                    this.closeReservationModal();
                    window.location.href = redirect || '/calendar';
                }
                else {
                    this.modalError = (await resp.text()).trim();
                }
            }
            catch (_) {
                this.modalError = 'Network error. Please try again.';
            }
        },
        startEdit: async function (reservationId) {
            this.closeDayPanel();
            this.editError = '';
            try {
                const resp = await fetch('/reservations/' + reservationId);
                if (!resp.ok)
                    throw new Error();
                const data = (await resp.json());
                this.editReservationId = reservationId;
                this.editGuestName = data.userName || '';
                this.editNotes = data.notes || '';
                // Date changes always start with a fresh check-in selection.
                this.selectedCheckIn = null;
                this.selectedCheckOut = null;
            }
            catch (_) {
                alert('Failed to load reservation details.');
            }
        },
        cancelEdit: function () {
            this.editReservationId = '';
            this.editGuestName = '';
            this.editNotes = '';
            this.editError = '';
            this.selectedCheckIn = null;
            this.selectedCheckOut = null;
        },
        editRangeLabel: function () {
            if (!this.selectedCheckIn)
                return '';
            if (!this.selectedCheckOut)
                return formatDate(this.selectedCheckIn) + ' → pick a check-out';
            return formatDate(this.selectedCheckIn) + ' → ' + formatDate(this.selectedCheckOut);
        },
        nightsAreFree: function (start, end) {
            for (let d = new Date(start + 'T12:00:00'), stop = new Date(end + 'T12:00:00'); d < stop; d.setDate(d.getDate() + 1)) {
                const key = d.getFullYear() +
                    '-' +
                    String(d.getMonth() + 1).padStart(2, '0') +
                    '-' +
                    String(d.getDate()).padStart(2, '0');
                const cell = document.querySelector('.day[data-date="' + key + '"]');
                if (!cell)
                    continue;
                if (!this.isSelectableForEdit(cell.dataset.resId || '', !!cell.dataset.blackout, !!cell.dataset.past))
                    return false;
            }
            return true;
        },
        saveEdit: async function () {
            if (!this.selectedCheckIn || !this.selectedCheckOut) {
                this.editError = 'Pick a check-out date.';
                return;
            }
            const body = new URLSearchParams({
                check_in: this.selectedCheckIn,
                check_out: this.selectedCheckOut,
                notes: this.editNotes,
            });
            try {
                const resp = await fetch('/reservations/' + this.editReservationId, {
                    method: 'PATCH',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'X-Requested-With': 'fetch',
                        'X-XSRF-TOKEN': xsrfToken(),
                    },
                    body: body.toString(),
                });
                if (resp.ok) {
                    const redirect = resp.headers.get('X-Redirect');
                    this.cancelEdit();
                    window.location.href = redirect || window.location.pathname;
                }
                else {
                    this.editError = (await resp.text()).trim() || 'Failed to save changes.';
                }
            }
            catch (_) {
                this.editError = 'Network error. Please try again.';
            }
        },
        cancelReservation: async function (url, message) {
            if (!confirm(message))
                return;
            try {
                const resp = await fetch(url, {
                    method: 'DELETE',
                    headers: { 'X-Requested-With': 'fetch', 'X-XSRF-TOKEN': xsrfToken() },
                });
                if (resp.ok) {
                    const redirect = resp.headers.get('X-Redirect');
                    window.location.href = redirect || window.location.pathname;
                }
                else {
                    alert((await resp.text()).trim() || 'Failed to cancel reservation.');
                }
            }
            catch (_) {
                alert('Network error. Please try again.');
            }
        },
        openGuestbookModal: function (reservationId, existingMessage) {
            this.guestbookReservationId = reservationId;
            this.guestbookMessage = existingMessage || '';
            this.guestbookError = '';
            this.$refs.guestbookModal.showModal();
        },
        submitGuestbook: async function () {
            this.guestbookError = '';
            const body = new URLSearchParams({
                reservation_id: this.guestbookReservationId ?? '',
                message: this.guestbookMessage,
            });
            try {
                const resp = await fetch('/reservations?handler=Guestbook', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'X-XSRF-TOKEN': xsrfToken(),
                    },
                    body: body.toString(),
                });
                if (resp.ok || resp.redirected) {
                    this.$refs.guestbookModal.close();
                    window.location.reload();
                }
                else {
                    this.guestbookError = (await resp.text()).trim() || 'Failed to save entry.';
                }
            }
            catch (_) {
                this.guestbookError = 'Network error. Please try again.';
            }
        },
        openDayPanel: function (el) {
            this.panelOpen = true;
            this.panelDate = el.dataset.date || '';
            this.panelGuestName = el.dataset.resName || '';
            this.panelCheckIn = el.dataset.resCheckin || '';
            this.panelCheckOut = el.dataset.resCheckout || '';
            this.panelReservationId = el.dataset.resId || '';
            this.panelCancelUrl = el.dataset.cancelUrl || '';
            this.panelCancelMsg = el.dataset.cancelMsg || '';
        },
        closeDayPanel: function () {
            this.panelOpen = false;
        },
    };
    return app;
}
function photoSorter(initialPhotos) {
    return {
        photos: initialPhotos,
        dragging: null,
        dirty: false,
        dragStart: function (i) {
            this.dragging = i;
        },
        dragOver: function (i) {
            if (this.dragging === null || this.dragging === i)
                return;
            const arr = this.photos.slice();
            const item = arr.splice(this.dragging, 1)[0];
            arr.splice(i, 0, item);
            this.photos = arr;
            this.dragging = i;
            this.dirty = true;
        },
        drop: function () {
            this.dragging = null;
        },
        dragEnd: function () {
            this.dragging = null;
        },
    };
}
function buildPopupNode(title, description) {
    const container = document.createElement('div');
    const strong = document.createElement('strong');
    strong.textContent = title;
    container.appendChild(strong);
    if (description) {
        container.appendChild(document.createElement('br'));
        container.appendChild(document.createTextNode(description));
    }
    return container;
}
function addOsmTiles(map) {
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
        maxZoom: 19,
    }).addTo(map);
}
// Leaflet's default marker, spelled out because the images come from the CDN, not from a
// bundled copy it can find on its own.
function propertyPinIcon() {
    return L.icon({
        iconUrl: 'https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/images/marker-icon.png',
        iconRetinaUrl: 'https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/images/marker-icon-2x.png',
        shadowUrl: 'https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/images/marker-shadow.png',
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
        shadowSize: [41, 41],
    });
}
function spotDivIcon() {
    return L.divIcon({
        className: '',
        html: '<svg xmlns="http://www.w3.org/2000/svg" width="25" height="41" viewBox="0 0 25 41">' +
            '<path fill="#2ecc71" stroke="#fff" stroke-width="1.5" d="M12.5 0C5.596 0 0 5.596 0 12.5c0 9.375 12.5 28.5 12.5 28.5S25 21.875 25 12.5C25 5.596 19.404 0 12.5 0z"/>' +
            '<circle fill="#fff" cx="12.5" cy="12.5" r="5"/>' +
            '</svg>',
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
    });
}
function initPropertyMap() {
    const mapEl = document.getElementById('property-map');
    if (!mapEl || typeof L === 'undefined')
        return;
    const raw = mapEl.getAttribute('data-map');
    if (!raw)
        return;
    const data = JSON.parse(raw);
    const map = L.map('property-map').setView([data.property.lat, data.property.lng], 14);
    window._propertyMap = map;
    addOsmTiles(map);
    const propertyIcon = propertyPinIcon();
    L.marker([data.property.lat, data.property.lng], { icon: propertyIcon })
        .addTo(map)
        .bindPopup(buildPopupNode(data.property.name, 'Your stay'));
    const bounds = L.latLngBounds([[data.property.lat, data.property.lng]]);
    let hasSpotsOrPaths = false;
    const spotMarkers = {};
    if (data.spots && data.spots.length > 0) {
        const spotIcon = spotDivIcon();
        data.spots.forEach(function (spot) {
            const marker = L.marker([spot.lat, spot.lng], { icon: spotIcon })
                .addTo(map)
                .bindPopup(buildPopupNode(spot.name, spot.description));
            spotMarkers[spot.lat.toFixed(6) + ',' + spot.lng.toFixed(6)] = marker;
            bounds.extend([spot.lat, spot.lng]);
        });
        hasSpotsOrPaths = true;
    }
    if (data.paths && data.paths.length > 0) {
        data.paths.forEach(function (path) {
            if (!path.coordinates || path.coordinates.length < 2)
                return;
            const latlngs = path.coordinates.map(function (c) {
                return [c[0], c[1]];
            });
            L.polyline(latlngs, {
                color: path.color || '#3388ff',
                weight: path.weight || 3,
                opacity: typeof path.opacity === 'number' ? path.opacity : 1.0,
            })
                .addTo(map)
                .bindPopup(buildPopupNode(path.name));
            latlngs.forEach(function (ll) {
                bounds.extend(ll);
            });
        });
        hasSpotsOrPaths = true;
    }
    if (hasSpotsOrPaths) {
        map.fitBounds(bounds, { padding: [50, 50], maxZoom: 15 });
    }
    document.querySelectorAll('.spot-chip--mappable').forEach(function (card) {
        card.addEventListener('click', function () {
            const key = parseFloat(card.getAttribute('data-lat') ?? '').toFixed(6) +
                ',' +
                parseFloat(card.getAttribute('data-lng') ?? '').toFixed(6);
            const m = spotMarkers[key];
            if (m) {
                map.setView(m.getLatLng(), 16);
                m.openPopup();
                requireEl('property-map').scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        });
    });
}
function initPathDrawingMap(centerLat, centerLng, zoom) {
    const mapEl = document.getElementById('path-drawing-map');
    if (!mapEl || typeof L === 'undefined')
        return;
    const map = L.map('path-drawing-map').setView([centerLat, centerLng], zoom);
    window._drawMap = map;
    addOsmTiles(map);
    let waypoints = [];
    let markers = [];
    const polyline = L.polyline([], { color: '#3388ff', weight: 3 }).addTo(map);
    const coordsInput = requireEl('path-coordinates');
    const countLabel = requireEl('path-point-count');
    const colorInput = document.getElementById('path-color');
    const weightInput = document.getElementById('path-weight');
    const undoBtn = document.getElementById('path-undo-btn');
    function syncStyle() {
        polyline.setStyle({
            color: colorInput ? colorInput.value : '#3388ff',
            weight: weightInput ? parseInt(weightInput.value, 10) || 3 : 3,
        });
    }
    if (colorInput)
        colorInput.addEventListener('input', syncStyle);
    if (weightInput)
        weightInput.addEventListener('input', syncStyle);
    function updateCoords() {
        coordsInput.value = JSON.stringify(waypoints);
        countLabel.textContent = waypoints.length + (waypoints.length === 1 ? ' point' : ' points');
        polyline.setLatLngs(waypoints);
        if (undoBtn)
            undoBtn.disabled = waypoints.length === 0;
    }
    function addWaypointMarker(latlng) {
        const m = L.circleMarker(latlng, {
            radius: 5,
            color: '#fff',
            weight: 2,
            fillColor: colorInput ? colorInput.value : '#3388ff',
            fillOpacity: 1,
        }).addTo(map);
        markers.push(m);
        return m;
    }
    function clearWaypoints() {
        waypoints = [];
        markers.forEach(function (m) {
            map.removeLayer(m);
        });
        markers = [];
        updateCoords();
    }
    function setWaypoints(points) {
        clearWaypoints();
        points.forEach(function (p) {
            const latlng = [p[0], p[1]];
            waypoints.push(latlng);
            addWaypointMarker(latlng);
        });
        updateCoords();
    }
    map._pathEditor = { setWaypoints: setWaypoints, clear: clearWaypoints };
    map.on('click', function (e) {
        const latlng = [
            parseFloat(e.latlng.lat.toFixed(6)),
            parseFloat(e.latlng.lng.toFixed(6)),
        ];
        waypoints.push(latlng);
        addWaypointMarker(latlng);
        updateCoords();
    });
    requireEl('path-clear-btn').addEventListener('click', clearWaypoints);
    if (undoBtn)
        undoBtn.addEventListener('click', function () {
            if (waypoints.length === 0)
                return;
            waypoints.pop();
            const last = markers.pop();
            if (last)
                map.removeLayer(last);
            updateCoords();
        });
    requireEl('add-path-form').addEventListener('submit', function (e) {
        e.preventDefault();
        if (waypoints.length < 2) {
            alert('Please draw a path with at least 2 points before saving.');
            return;
        }
        const errorEl = document.getElementById('path-error');
        if (errorEl)
            errorEl.style.display = 'none';
        void adminSubmit(e.target).then(function (err) {
            if (err && errorEl) {
                errorEl.textContent = err;
                errorEl.style.display = '';
            }
        });
    });
}
function enterPathEditMode(data) {
    const form = document.getElementById('add-path-form');
    if (!form)
        return;
    requireEl('path-id').value = data.id;
    requireEl('path-name').value = data.name;
    const colorInput = requireEl('path-color');
    colorInput.value = data.color;
    colorInput.dispatchEvent(new Event('input'));
    const weightInput = requireEl('path-weight');
    weightInput.value = data.weight;
    weightInput.dispatchEvent(new Event('input'));
    const opacityInput = requireEl('path-opacity');
    opacityInput.value = data.opacity;
    opacityInput.dispatchEvent(new Event('input'));
    window._drawMap?._pathEditor?.setWaypoints(JSON.parse(data.coordinates));
    form.action = form.action.replace('handler=AddPath', 'handler=UpdatePath');
    requireEl('path-submit-btn').textContent = 'Save Path';
    requireEl('path-cancel-btn').style.display = '';
    requireEl('path-error').style.display = 'none';
}
function exitPathEditMode() {
    const form = document.getElementById('add-path-form');
    if (!form)
        return;
    requireEl('path-id').value = '';
    requireEl('path-name').value = '';
    const colorInput = requireEl('path-color');
    colorInput.value = '#3388ff';
    colorInput.dispatchEvent(new Event('input'));
    const weightInput = requireEl('path-weight');
    weightInput.value = '3';
    weightInput.dispatchEvent(new Event('input'));
    const opacityInput = requireEl('path-opacity');
    opacityInput.value = '1.0';
    opacityInput.dispatchEvent(new Event('input'));
    window._drawMap?._pathEditor?.clear();
    form.action = form.action.replace('handler=UpdatePath', 'handler=AddPath');
    requireEl('path-submit-btn').textContent = '+ Add Path';
    requireEl('path-cancel-btn').style.display = 'none';
    requireEl('path-error').style.display = 'none';
}
document.addEventListener('click', function (e) {
    const target = e.target;
    const editBtn = target?.closest('.path-edit-btn');
    if (editBtn) {
        enterPathEditMode({
            id: editBtn.dataset.id ?? '',
            name: editBtn.dataset.name ?? '',
            color: editBtn.dataset.color ?? '',
            weight: editBtn.dataset.weight ?? '',
            opacity: editBtn.dataset.opacity ?? '',
            coordinates: editBtn.dataset.coordinates ?? '',
        });
        return;
    }
    if (target?.closest('#path-cancel-btn'))
        exitPathEditMode();
});
function initAdminSpotsMap() {
    const mapEl = document.getElementById('admin-spots-map');
    if (!mapEl || typeof L === 'undefined')
        return;
    const raw = mapEl.getAttribute('data-map');
    if (!raw)
        return;
    const data = JSON.parse(raw);
    const map = L.map('admin-spots-map').setView([data.property.lat, data.property.lng], 14);
    window._spotsMap = map;
    addOsmTiles(map);
    const propertyIcon = propertyPinIcon();
    L.marker([data.property.lat, data.property.lng], { icon: propertyIcon })
        .addTo(map)
        .bindPopup(buildPopupNode(data.property.name));
    const bounds = L.latLngBounds([[data.property.lat, data.property.lng]]);
    if (data.spots && data.spots.length > 0) {
        const spotIcon = spotDivIcon();
        data.spots.forEach(function (spot) {
            L.marker([spot.lat, spot.lng], { icon: spotIcon })
                .addTo(map)
                .bindPopup(buildPopupNode(spot.name, spot.description));
            bounds.extend([spot.lat, spot.lng]);
        });
        map.fitBounds(bounds, { padding: [50, 50], maxZoom: 15 });
    }
}
document.addEventListener('DOMContentLoaded', function () {
    if (typeof L === 'undefined')
        return;
    initAdminSpotsMap();
    const drawEl = document.getElementById('path-drawing-map');
    if (drawEl) {
        initPathDrawingMap(parseFloat(drawEl.getAttribute('data-lat') || '20'), parseFloat(drawEl.getAttribute('data-lng') || '0'), parseInt(drawEl.getAttribute('data-zoom') || '2', 10));
    }
});
document.addEventListener('click', function (e) {
    const target = e.target;
    const img = target?.closest('.gallery-img');
    if (img) {
        const modal = document.getElementById('lightbox-modal');
        const lightboxImg = document.getElementById('lightbox-img');
        if (modal && lightboxImg) {
            lightboxImg.src = img.src;
            lightboxImg.alt = img.alt;
            modal.showModal();
        }
    }
});
