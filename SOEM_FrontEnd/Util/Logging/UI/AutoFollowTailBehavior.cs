using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SOEM_FrontEnd.Util.Logging.UI
{
    public static class AutoFollowTailBehavior
    {
        // AttachedProperty owner는 AvaloniaObject 계열이어야 함 (static class 불가)
        private sealed class Owner : AvaloniaObject { }

        //Enable/Disable
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Owner, ListBox, bool>(
                "IsEnabled", defaultValue: false);

        //바닥 근처 판정값
        public static readonly AttachedProperty<double> ThresholdPxProperty =
            AvaloniaProperty.RegisterAttached<Owner, ListBox, double>(
                "ThresholdPx", defaultValue: 24);

        private sealed class State
        {
            public bool FollowTail = true;
            public ScrollViewer Scroll;
            public NotifyCollectionChangedEventHandler CollectionHandler;
            public object LastItemsSource;
        }

        private static readonly ConditionalWeakTable<ListBox, State> _states =
            new ConditionalWeakTable<ListBox, State>();

        static AutoFollowTailBehavior()
        {
            // Rx 없이, ClassHandler로 attached property 변경 감지
            IsEnabledProperty.Changed.AddClassHandler<ListBox>(OnIsEnabledChanged);
        }

        public static bool GetIsEnabled(AvaloniaObject obj) => obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(AvaloniaObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        public static double GetThresholdPx(AvaloniaObject obj) => obj.GetValue(ThresholdPxProperty);
        public static void SetThresholdPx(AvaloniaObject obj, double value) => obj.SetValue(ThresholdPxProperty, value);

        private static void OnIsEnabledChanged(ListBox lb, AvaloniaPropertyChangedEventArgs e)
        {
            bool enabled = e.NewValue is bool b && b;

            if (enabled) Attach(lb);
            else Detach(lb);
        }

        private static void Attach(ListBox lb)
        {
            Detach(lb);

            var st = new State();
            _states.Add(lb, st);

            // ListBox 생명주기 이벤트
            lb.AttachedToVisualTree += Lb_AttachedToVisualTree;
            lb.DetachedFromVisualTree += Lb_DetachedFromVisualTree;
            lb.PropertyChanged += Lb_PropertyChanged;

            // ItemsSource 현재값에 대해 1회 연결
            RewireItemsSource(lb, st, lb.ItemsSource);
        }

        private static void Detach(ListBox lb)
        {
            lb.AttachedToVisualTree -= Lb_AttachedToVisualTree;
            lb.DetachedFromVisualTree -= Lb_DetachedFromVisualTree;
            lb.PropertyChanged -= Lb_PropertyChanged;

            if (_states.TryGetValue(lb, out var st))
            {
                UnhookScrollViewer(st);
                UnhookCollection(st);
                _states.Remove(lb);
            }
        }

        private static void Lb_AttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            var lb = (ListBox)sender;
            if (!_states.TryGetValue(lb, out var st)) return;

            HookScrollViewer(lb, st);

            // 초기엔 보통 tail follow 상태로 끝으로 한번
            ScrollToEnd(lb, force: true);
        }

        private static void Lb_DetachedFromVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            var lb = (ListBox)sender;
            if (!_states.TryGetValue(lb, out var st)) return;

            UnhookScrollViewer(st);
        }

        private static void Lb_PropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            var lb = (ListBox)sender;
            if (!_states.TryGetValue(lb, out var st)) return;

            if (e.Property == ItemsControl.ItemsSourceProperty)
            {
                RewireItemsSource(lb, st, e.NewValue);
            }
            else if (e.Property == ThresholdPxProperty)
            {
                // threshold 바뀌면 follow 상태 재판정
                UpdateFollowTail(lb, st);
            }
        }

        private static void HookScrollViewer(ListBox lb, State st)
        {
            UnhookScrollViewer(st);

            // ListBox 내부 ScrollViewer 찾기
            var sv = lb.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (sv == null)
            {
                // 템플릿이 아직 안 붙었으면 다음 tick에 재시도
                Dispatcher.UIThread.Post(() => HookScrollViewer(lb, st), DispatcherPriority.Background);
                return;
            }

            st.Scroll = sv;
            sv.ScrollChanged += Sv_ScrollChanged;

            UpdateFollowTail(lb, st);
        }

        private static void UnhookScrollViewer(State st)
        {
            if (st.Scroll != null)
            {
                st.Scroll.ScrollChanged -= Sv_ScrollChanged;
                st.Scroll = null;
            }
        }

        private static void Sv_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // sender는 ScrollViewer
            // ListBox가 필요하므로, 전체 states에서 해당 ScrollViewer를 가진 ListBox를 찾는 대신
            // FollowTail 판정은 "scroll 변화 때" ListBox 기준이 필요.
            // 여기서는 모든 ListBox를 훑지 않고, Offset/Extent/Viewport만 바뀌었으니
            // FollowTail 재판정은 UpdateFollowTail에서 수행 (ListBox + State 필요)
            //
            // 사용자가 스크롤 내렸는지 여부는 'FollowTail'이 계속 true로 유지되면 자동 스크롤이 걸림.
        }

        private static void RewireItemsSource(ListBox lb, State st, object newSource)
        {
            if (ReferenceEquals(st.LastItemsSource, newSource)) return;

            UnhookCollection(st);

            st.LastItemsSource = newSource;

            var incc = newSource as INotifyCollectionChanged;
            if (incc == null) return;

            st.CollectionHandler = (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add ||
                    args.Action == NotifyCollectionChangedAction.Reset ||
                    args.Action == NotifyCollectionChangedAction.Replace)
                {
                    // 바닥 근처일 때만 자동 스크롤
                    if (IsNearBottom(lb))
                        ScrollToEnd(lb, force: false);
                }
            };

            incc.CollectionChanged += st.CollectionHandler;
        }

        private static void UnhookCollection(State st)
        {
            var incc = st.LastItemsSource as INotifyCollectionChanged;
            if (incc != null && st.CollectionHandler != null)
            {
                incc.CollectionChanged -= st.CollectionHandler;
            }

            st.CollectionHandler = null;
            st.LastItemsSource = null;
        }

        private static bool IsNearBottom(ListBox lb)
        {
            // ScrollViewer를 즉시 못 찾는 경우도 있으니 안전하게
            var sv = lb.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (sv == null) return true; // 못 찾으면 일단 따라가게(초기 로딩용)

            double threshold = lb.GetValue(ThresholdPxProperty);

            double offsetY = sv.Offset.Y;
            double viewportH = sv.Viewport.Height;
            double extentH = sv.Extent.Height;

            return (offsetY + viewportH) >= (extentH - threshold);
        }

        private static void UpdateFollowTail(ListBox lb, State st)
        {
            st.FollowTail = IsNearBottom(lb);
        }

        private static void ScrollToEnd(ListBox lb, bool force)
        {
            if (!force)
            {
                // 바닥 근처가 아니면 스크롤 안 따라감
                if (!IsNearBottom(lb))
                    return;
            }

            var list = lb.ItemsSource as IList;
            if (list == null || list.Count <= 0) return;

            var last = list[list.Count - 1];

            Dispatcher.UIThread.Post(() =>
            {
                try { lb.ScrollIntoView(last); }
                catch { /* 실패해도 죽지 않게만.. */ }
            }, DispatcherPriority.Background);
        }
    }
}